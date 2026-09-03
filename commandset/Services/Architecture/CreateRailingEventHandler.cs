using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Architecture
{
    public class CreateRailingEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;


        public List<RailingCreationInfo> RailingData { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        private List<string> _warnings = new List<string>();

        public void SetParameters(List<RailingCreationInfo> data)
        {
            RailingData = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();

                foreach (var info in RailingData)
                {
                    Level level = FindNearestLevel(info.Level / 304.8);
                    if (level == null)
                    {
                        _warnings.Add("No levels found in document; railing skipped");
                        continue;
                    }

                    RailingType railingType = null;
                    if (info.TypeId > 0)
                    {
                        railingType = _doc.GetElement(ElementIdFactory.Create(info.TypeId)) as RailingType;
                    }

                    if (railingType == null && !string.IsNullOrEmpty(info.RailingType))
                    {
                        using (var fec = new FilteredElementCollector(_doc))
                        {
                            railingType = fec
                                .OfClass(typeof(RailingType))
                                .Cast<RailingType>()
                                .FirstOrDefault(rt => rt.Name.Equals(info.RailingType, StringComparison.OrdinalIgnoreCase));
                        }
                        if (railingType == null)
                        {
                            _warnings.Add($"Railing type '{info.RailingType}' not found, using first available");
                        }
                    }

                    if (railingType == null)
                    {
                        using (var fec = new FilteredElementCollector(_doc))
                        {
                            railingType = fec
                                .OfClass(typeof(RailingType))
                                .Cast<RailingType>()
                                .FirstOrDefault();
                        }
                    }

                    if (railingType == null)
                    {
                        _warnings.Add("No railing types available in document; railing skipped");
                        continue;
                    }

                    using (Transaction tx = new Transaction(_doc, "Create Railing"))
                    {
                        tx.Start();

                        try
                        {
                            // Build railing path line
                            Line pathLine = null;
                            if (info.StartPoint != null && info.EndPoint != null)
                            {
                                XYZ start = JZPoint.ToXYZ(info.StartPoint);
                                XYZ end = JZPoint.ToXYZ(info.EndPoint);
                                pathLine = Line.CreateBound(start, end);
                            }
                            else if (info.PathPoints != null && info.PathPoints.Count >= 2)
                            {
                                XYZ start = JZPoint.ToXYZ(info.PathPoints[0]);
                                XYZ end = JZPoint.ToXYZ(info.PathPoints[info.PathPoints.Count - 1]);
                                pathLine = Line.CreateBound(start, end);
                            }

                            if (pathLine == null)
                            {
                                _warnings.Add("No valid path defined for railing; railing skipped");
                                continue;
                            }

                            // Railing.Create takes CurveLoop in all supported Revit versions (2022+)
                            CurveLoop curveLoop = new CurveLoop();
                            curveLoop.Append(pathLine);
                            Railing railing = Railing.Create(_doc, curveLoop, railingType.Id, level.Id);

                            if (railing != null)
                            {
                                // Set railing height if specified
                                // BuiltInParameter.RAILING_HEIGHT does not exist in any Revit version;
                                // use LookupParameter("Height") instead.
                                if (info.Height > 0 && info.Height != 1070)
                                {
                                    Parameter heightParam = railing.LookupParameter("Height");
                                    if (heightParam != null && !heightParam.IsReadOnly)
                                    {
                                        heightParam.Set(info.Height / 304.8);
                                    }
                                }

                                elementIds.Add(railing.Id.GetIntValue());
                            }

                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            _warnings.Add($"Failed to create railing: {ex.Message}");
                        }
                    }
                }

                if (elementIds.Count == 0 && RailingData.Count > 0)
                {
                    string failMsg = "No railings were created.";
                    if (_warnings.Count > 0)
                        failMsg += " " + string.Join("; ", _warnings);
                    else
                        failMsg += " All railing definitions were skipped.";
                    Result = new AIResult<List<int>>
                    {
                        Success = false,
                        Message = failMsg,
                        Response = elementIds
                    };
                }
                else
                {
                    string message = $"Successfully created {elementIds.Count} railing(s)";
                    if (_warnings.Count > 0)
                    {
                        message += "\nWarnings:\n  " + string.Join("\n  ", _warnings);
                    }

                    Result = new AIResult<List<int>>
                    {
                        Success = true,
                        Message = message,
                        Response = elementIds
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating railings: {ex.Message}",
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
            return "Create Railing";
        }
    }
}
