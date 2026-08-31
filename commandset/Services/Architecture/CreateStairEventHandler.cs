using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Architecture
{
    /// <summary>
    /// Clears warnings raised while a StairsEditScope commits, and counts them so the
    /// caller can be told. Errors are left alone: resolving those automatically is how
    /// an invalid stair ends up committed with nobody informed.
    /// </summary>
    public class StairWarningSwallower : IFailuresPreprocessor
    {
        public int WarningsCleared { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() != FailureSeverity.Warning) continue;
                accessor.DeleteWarning(failure);
                WarningsCleared++;
            }

            return FailureProcessingResult.Continue;
        }
    }

    public class CreateStairEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;


        public List<StairCreationInfo> StairData { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        private List<string> _warnings = new List<string>();

        public void SetParameters(List<StairCreationInfo> data)
        {
            StairData = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();

                foreach (var info in StairData)
                {
                    Level baseLevel = FindNearestLevel(info.BaseLevel / 304.8);
                    Level topLevel = FindNearestLevel(info.TopLevel / 304.8);
                    if (baseLevel == null || topLevel == null) continue;

                    using (Transaction tx = new Transaction(_doc, "Create Stair"))
                    {
                        tx.Start();

                        try
                        {
#if REVIT2022_OR_GREATER
                            // Measured, not assumed: StairsEditScope.Start and
                            // StairsRun.CreateStraightRun are IDENTICAL across Revit
                            // 2022-2027. The branch that used to stand here refused on
                            // 2026 with "Stairs API changed significantly" - it had not.
                            // The old #elif also gated the working implementation behind
                            // REVIT2025_OR_GREATER while its #else claimed to require
                            // 2022, so 2022-2024 silently got the refusal path.
                            // StairsEditScope cannot be started while a transaction is open.
                            // Roll back the outer wrapper transaction before entering the edit scope.
                            tx.RollBack();
                            try
                            {
                                StairsType stairsType = null;
                                if (info.TypeId > 0)
                                {
                                    stairsType = _doc.GetElement(ElementIdFactory.Create(info.TypeId)) as StairsType;
                                }

                                if (stairsType == null && !string.IsNullOrEmpty(info.StairType))
                                {
                                    using (var fec = new FilteredElementCollector(_doc))
                                    {
                                        stairsType = fec
                                            .OfClass(typeof(StairsType))
                                            .Cast<StairsType>()
                                            .FirstOrDefault(st => st.Name.Equals(info.StairType, StringComparison.OrdinalIgnoreCase));
                                    }
                                    if (stairsType == null)
                                    {
                                        _warnings.Add($"Stair type '{info.StairType}' not found, using default");
                                    }
                                }

                                double widthInFeet = info.Width / 304.8;

                                // StairsEditScope manages its own transaction group. Start(baseLevelId, topLevelId)
                                // creates a new Stairs element and returns its ElementId. Runs and landings are
                                // added inside an inner transaction referencing that stairsId.
                                using (StairsEditScope stairsScope = new StairsEditScope(_doc, "Create Stair"))
                                {
                                    ElementId stairsId = stairsScope.Start(baseLevel.Id, topLevel.Id);
                                    bool scopeCommitted = false;

                                    using (Transaction stairsTx = new Transaction(_doc, "Add Stair Components"))
                                    {
                                        stairsTx.Start();
                                        try
                                        {
                                            // Apply the requested type if one was found.
                                            if (stairsType != null)
                                            {
                                                _doc.GetElement(stairsId).ChangeTypeId(stairsType.Id);
                                            }

                                            int runCount = 0;

                                            if (info.PathPoints != null && info.PathPoints.Count >= 2)
                                            {
                                                for (int i = 0; i < info.PathPoints.Count - 1; i++)
                                                {
                                                    XYZ startPt = JZPoint.ToXYZ(info.PathPoints[i]);
                                                    XYZ endPt = JZPoint.ToXYZ(info.PathPoints[i + 1]);
                                                    Line runLine = Line.CreateBound(startPt, endPt);
                                                    StairsRun.CreateStraightRun(_doc, stairsId, runLine, StairsRunJustification.Center);
                                                    runCount++;
                                                }
                                            }
                                            else if (info.StartPoint != null && info.EndPoint != null)
                                            {
                                                XYZ startPt = JZPoint.ToXYZ(info.StartPoint);
                                                XYZ endPt = JZPoint.ToXYZ(info.EndPoint);
                                                Line runLine = Line.CreateBound(startPt, endPt);
                                                StairsRun.CreateStraightRun(_doc, stairsId, runLine, StairsRunJustification.Center);
                                                runCount++;
                                            }

                                            if (runCount == 0)
                                            {
                                                stairsTx.RollBack();
                                            }
                                            else
                                            {
                                                if (info.HasLanding && runCount > 1 && info.PathPoints != null && info.PathPoints.Count > 1)
                                                {
                                                    double lw = info.LandingWidth > 0 ? info.LandingWidth / 304.8 : widthInFeet;
                                                    double ld = info.LandingDepth > 0 ? info.LandingDepth / 304.8 : widthInFeet;
                                                    XYZ lOrigin = JZPoint.ToXYZ(info.PathPoints[1]);
                                                    CurveLoop landingLoop = new CurveLoop();
                                                    landingLoop.Append(Line.CreateBound(lOrigin, new XYZ(lOrigin.X + lw, lOrigin.Y, lOrigin.Z)));
                                                    landingLoop.Append(Line.CreateBound(new XYZ(lOrigin.X + lw, lOrigin.Y, lOrigin.Z), new XYZ(lOrigin.X + lw, lOrigin.Y + ld, lOrigin.Z)));
                                                    landingLoop.Append(Line.CreateBound(new XYZ(lOrigin.X + lw, lOrigin.Y + ld, lOrigin.Z), new XYZ(lOrigin.X, lOrigin.Y + ld, lOrigin.Z)));
                                                    landingLoop.Append(Line.CreateBound(new XYZ(lOrigin.X, lOrigin.Y + ld, lOrigin.Z), lOrigin));
                                                    // baseElevation is relative to the stairs base elevation; 0.0 places the landing at the base.
                                                    StairsLanding.CreateSketchedLanding(_doc, stairsId, landingLoop, 0.0);
                                                }

                                                stairsTx.Commit();
                                                // EditScope.Commit takes an IFailuresPreprocessor, not a
                                                // FailureHandlingOptions - the latter has no public constructor
                                                // and belongs to Transaction, not to an edit scope.
                                                //
                                                // Not DeleteWarningSuperUtils: that also auto-RESOLVES errors,
                                                // which for stairs would let a genuinely invalid run commit
                                                // silently. This one clears warnings (stairs raise them
                                                // routinely) and leaves errors to Revit, then reports what it
                                                // cleared instead of swallowing it.
                                                var stairFailures = new StairWarningSwallower();
                                                stairsScope.Commit(stairFailures);
                                                if (stairFailures.WarningsCleared > 0)
                                                    _warnings.Add(
                                                        $"Stair created with {stairFailures.WarningsCleared} " +
                                                        "Revit warning(s) dismissed.");
                                                scopeCommitted = true;
                                                elementIds.Add(stairsId.GetIntValue());
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            if (stairsTx.GetStatus() == TransactionStatus.Started)
                                                stairsTx.RollBack();
                                            _warnings.Add($"Failed to add stair components: {ex.Message}");
                                        }
                                    }

                                    if (!scopeCommitted)
                                        stairsScope.Cancel();
                                }
                            }
                            catch (Exception ex)
                            {
                                _warnings.Add($"Failed to create stair: {ex.Message}");
                            }
                            continue;
#else
                            _warnings.Add("Stair creation requires Revit 2022 or later");
                            tx.RollBack();
                            continue;
#endif

                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            _warnings.Add($"Failed to create stair: {ex.Message}");
                        }
                    }
                }

                string message = $"Successfully created {elementIds.Count} stair(s)";
                if (_warnings.Count > 0)
                {
                    message += "\nWarnings:\n  " + string.Join("\n  ", _warnings);
                }

                // Success is CONDITIONAL on something having been created. The handler
                // used to report Success = true with an empty list whenever every stair
                // warned and rolled back, so a caller was told the work was done.
                Result = new AIResult<List<int>>
                {
                    Success = elementIds.Count > 0,
                    Message = elementIds.Count > 0
                        ? message
                        : "No stair was created. " + (_warnings.Count > 0
                            ? string.Join(" ", _warnings)
                            : "No reason was recorded."),
                    Response = elementIds
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating stairs: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private Level FindNearestLevel(double elevationInFeet)
        {
            List<Level> levels;
            using (var fec = new FilteredElementCollector(_doc))
            {
                levels = fec
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();
            }

            Level nearestLevel = null;
            double minDistance = double.MaxValue;

            foreach (var level in levels)
            {
                double distance = Math.Abs(level.Elevation - elevationInFeet);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestLevel = level;
                }
            }

            return nearestLevel;
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Create Stair";
        }
    }
}
