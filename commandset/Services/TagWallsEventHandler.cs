using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class TagWallsEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

        /// <summary>
        /// Event wait handle
        /// </summary>

        /// <summary>
        /// Tag result data
        /// </summary>
        public object TaggingResults { get; private set; }

        private bool _useLeader;
        private string _tagTypeId;

        /// <summary>
        /// Set parameters for tag creation
        /// </summary>
        public void SetParameters(bool useLeader, string tagTypeId)
        {
            _useLeader = useLeader;
            _tagTypeId = tagTypeId;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                View activeView = doc.ActiveView;

                // Guard: tags cannot be created in a 3D view
                if (activeView is View3D || activeView.ViewType == ViewType.ThreeD)
                {
                    TaggingResults = new
                    {
                        success = false,
                        message = "Tags can only be created in a 2D view"
                    };
                    return;
                }

                // Get all walls in the current view
                using FilteredElementCollector wallCollector = new FilteredElementCollector(doc, activeView.Id);
                ICollection<Element> walls = wallCollector.OfCategory(BuiltInCategory.OST_Walls)
                                                         .WhereElementIsNotElementType()
                                                         .ToElements();

                // Create wall tags
                List<object> createdTags = new List<object>();
                List<string> errors = new List<string>();

                using (Transaction tran = new Transaction(doc, "Tag Walls"))
                {
                    tran.Start();

                    // Find the wall tag type
                    FamilySymbol wallTagType = FindWallTagType(doc);

                    if (wallTagType == null)
                    {
                        TaggingResults = new
                        {
                            success = false,
                            message = "No wall tag family type found"
                        };
                        tran.RollBack();
                        return;
                    }

                    // Ensure tag type is active
                    if (!wallTagType.IsActive)
                    {
                        wallTagType.Activate();
                        doc.Regenerate();
                    }

                    // Create tags for each wall
                    foreach (Element wall in walls)
                    {
#if REVIT2024_OR_GREATER
                        try
                        {
                            // Get the wall's location curve
                            LocationCurve locationCurve = wall.Location as LocationCurve;
                            if (locationCurve != null)
                            {
                                // Get the middle point of the wall
                                Curve curve = locationCurve.Curve;
                                XYZ midpoint = curve.Evaluate(0.5, true);

                                // Create tag at midpoint
                                IndependentTag tag = IndependentTag.Create(
                                    doc,
                                    wallTagType.Id,
                                    activeView.Id,
                                    new Reference(wall),
                                    _useLeader, // Use leader based on parameter
                                    TagOrientation.Horizontal,
                                    midpoint);

                                if (tag != null)
                                {
                                    createdTags.Add(new
                                    {
                                        id = tag.Id.Value.ToString(),
                                        wallId = wall.Id.Value.ToString(),

                                        wallName = wall.Name,
                                        location = new
                                        {
                                            x_mm = midpoint.X * 304.8,
                                            y_mm = midpoint.Y * 304.8,
                                            z_mm = midpoint.Z * 304.8
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error tagging wall {wall.Id.Value}: {ex.Message}");
                        }
#else
try
                        {
                            // Get the wall's location curve
                            LocationCurve locationCurve = wall.Location as LocationCurve;
                            if (locationCurve != null)
                            {
                                // Get the middle point of the wall
                                Curve curve = locationCurve.Curve;
                                XYZ midpoint = curve.Evaluate(0.5, true);

                                // Create tag at midpoint
                                IndependentTag tag = IndependentTag.Create(
                                    doc,
                                    wallTagType.Id,
                                    activeView.Id,
                                    new Reference(wall),
                                    _useLeader, // Use leader based on parameter
                                    TagOrientation.Horizontal,
                                    midpoint);

                                if (tag != null)
                                {
                                    createdTags.Add(new
                                    {
                                        id = tag.Id.GetIntValue().ToString(),
                                        wallId = wall.Id.GetIntValue().ToString(),

                                        wallName = wall.Name,
                                        location = new
                                        {
                                            x_mm = midpoint.X * 304.8,
                                            y_mm = midpoint.Y * 304.8,
                                            z_mm = midpoint.Z * 304.8
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error tagging wall {wall.Id.GetIntValue()}: {ex.Message}");
                        }
#endif
                    }

                    tran.Commit();

                    TaggingResults = new
                    {
                        success = true,
                        totalWalls = walls.Count,
                        taggedWalls = createdTags.Count,
                        tags = createdTags,
                        errors = errors.Count > 0 ? errors : null
                    };
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Report("Error", $"Error tagging walls: {ex.Message}");
                TaggingResults = new
                {
                    success = false,
                    message = $"An error occurred: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set(); // Notify the waiting thread that the operation is complete
            }
        }

        /// <summary>
        /// Wait for creation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds</param>
        /// <returns>Whether the operation completed before the timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Tag Walls";
        }

        /// <summary>
        /// Find the wall tag type in the document
        /// </summary>
        private FamilySymbol FindWallTagType(Document doc)
        {
            if (!string.IsNullOrEmpty(_tagTypeId))
            {
                if (int.TryParse(_tagTypeId, out int id))
                {
                    ElementId elementId = ElementIdFactory.Create(id);
                    Element element = doc.GetElement(elementId);

                    if (element != null && element is FamilySymbol symbol &&
                        (symbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_WallTags ||
                         symbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_MultiCategoryTags))
                    {
                        return symbol;
                    }
                }
            }

            FamilySymbol wallTagType;
            using (var tagCollector = new FilteredElementCollector(doc))
            {
                wallTagType = tagCollector.OfClass(typeof(FamilySymbol))
                                                  .WhereElementIsElementType()
                                                  .Where(e => e.Category != null &&
                                                         e.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_WallTags)
                                                  .Cast<FamilySymbol>()
                                                  .FirstOrDefault();
            }

            if (wallTagType == null)
            {
                using (var tagCollector2 = new FilteredElementCollector(doc))
                {
                    wallTagType = tagCollector2.OfClass(typeof(FamilySymbol))
                                             .WhereElementIsElementType()
                                             .Where(e => e.Category != null &&
                                                    e.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_MultiCategoryTags)
                                             .Cast<FamilySymbol>()
                                             .FirstOrDefault();
                }
            }

            return wallTagType;
        }
    }
}