using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Query
{
    public class CheckInterferencesEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int[] ElementIds { get; private set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(int[] elementIds)
        {
            ElementIds = elementIds;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                if (ElementIds == null || ElementIds.Length < 2)
                {
                    Result = new AIResult<object> { Success = false, Message = "At least two element IDs required for interference check" };
                    return;
                }
                var elementIds = ElementIds.Select(id => ElementIdFactory.Create(id)).ToList();
                var collisions = new List<object>();
                var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                // Pre-filter to elements that actually resolve to a solid.
                // TotalPairsChecked must reflect only pairs that were truly evaluated.
                var validElements = new List<Tuple<ElementId, Element, Solid>>();
                var skippedIds = new List<int>();
                for (int i = 0; i < elementIds.Count; i++)
                {
                    var elem = Doc.GetElement(elementIds[i]);
                    if (elem == null) { skippedIds.Add(ElementIds[i]); continue; }
                    var geom = elem.get_Geometry(options);
                    Solid solid = GetFirstSolid(geom);
                    if (solid == null) { skippedIds.Add(ElementIds[i]); continue; }
                    validElements.Add(Tuple.Create(elementIds[i], elem, solid));
                }

                for (int i = 0; i < validElements.Count; i++)
                {
                    var id1 = validElements[i].Item1;
                    var elem1 = validElements[i].Item2;
                    var solid1 = validElements[i].Item3;

                    for (int j = i + 1; j < validElements.Count; j++)
                    {
                        var id2 = validElements[j].Item1;
                        var elem2 = validElements[j].Item2;
                        var solid2 = validElements[j].Item3;

                        bool overlaps = false;
                        string intersectionType = "Unknown";
                        try
                        {
                            Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                                solid1, solid2, BooleanOperationsType.Intersect);
                            overlaps = intersection != null && intersection.Volume > 1e-9;
                            if (overlaps) intersectionType = "Overlap";
                        }
                        catch (InvalidOperationException)
                        {
                            // Geometric incompatibility prevents the Boolean operation;
                            // treat as no overlap rather than a hard failure.
                        }
                        if (overlaps)
                        {
                            collisions.Add(new
                            {
                                ElementId1 = id1.GetIntValue(),
                                ElementId2 = id2.GetIntValue(),
                                IntersectionType = intersectionType,
                                Element1Name = elem1.Name,
                                Element2Name = elem2.Name,
                                Element1Category = elem1.Category?.Name,
                                Element2Category = elem2.Category?.Name
                            });
                        }
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Response = new
                    {
                        TotalPairsChecked = (validElements.Count * (validElements.Count - 1)) / 2,
                        CollisionCount = collisions.Count,
                        Collisions = collisions,
                        SkippedElementIds = skippedIds.Count > 0 ? (object)skippedIds : null
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private Solid GetFirstSolid(GeometryElement geomElement)
        {
            if (geomElement == null) return null;
            foreach (var geomObj in geomElement)
            {
                if (geomObj is Solid solid && solid.Faces.Size > 0)
                    return solid;
                if (geomObj is GeometryInstance instance)
                {
                    var result = GetFirstSolid(instance.GetInstanceGeometry());
                    if (result != null) return result;
                }
            }
            return null;
        }

        public bool WaitForCompletion(int timeout = 30000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Check Interferences";
    }
}
