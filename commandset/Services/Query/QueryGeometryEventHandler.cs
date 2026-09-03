using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Query
{
    public class QueryGeometryEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int ElementId { get; private set; }
        public int? ViewId { get; private set; }
        public int? DetailLevel { get; private set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(int elementId, int? viewId, int? detailLevel)
        {
            ElementId = elementId;
            ViewId = viewId;
            DetailLevel = detailLevel;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var element = Doc.GetElement(ElementIdFactory.Create(ElementId));
                if (element == null)
                {
                    Result = new AIResult<object> { Success = false, Message = $"Element {ElementId} not found" };
                    return;
                }
                var options = new Options();
                if (ViewId.HasValue)
                {
                    options.View = Doc.GetElement(ElementIdFactory.Create(ViewId.Value)) as View;
                    if (options.View == null)
                    {
                        Result = new AIResult<object> { Success = false, Message = $"ViewId {ViewId.Value} does not refer to a valid View element" };
                        return;
                    }
                }
                if (DetailLevel.HasValue)
                    options.DetailLevel = (ViewDetailLevel)DetailLevel.Value;
                options.ComputeReferences = true;

                var geom = element.get_Geometry(options);
                var solids = new List<object>();
                var boundingBox = element.get_BoundingBox(null);
                var boundingBoxData = boundingBox != null ? new
                {
                    MinMm = new { X = boundingBox.Min.X * 304.8, Y = boundingBox.Min.Y * 304.8, Z = boundingBox.Min.Z * 304.8 },
                    MaxMm = new { X = boundingBox.Max.X * 304.8, Y = boundingBox.Max.Y * 304.8, Z = boundingBox.Max.Z * 304.8 }
                } : null;

                if (geom != null)
                {
                    CollectSolids(geom, solids);
                }

                var result = new
                {
                    ElementId = ElementId,
                    BoundingBox = boundingBoxData,
                    SolidCount = solids.Count,
                    Solids = solids
                };
                Result = new AIResult<object> { Success = true, Response = result };
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

        private void CollectSolids(GeometryElement geomElement, List<object> solids)
        {
            foreach (var geomObj in geomElement)
            {
                if (geomObj is Solid solid && solid.Faces.Size > 0)
                {
                    var faceList = new List<object>();
                    foreach (Face face in solid.Faces)
                    {
                        faceList.Add(new
                        {
                            AreaM2 = face.Area * 0.09290304,
                            SurfaceType = VersionCompat.GetSurfaceTypeName(face),
                            EdgeCount = face.EdgeLoops.Size
                        });
                    }
                    solids.Add(new
                    {
                        VolumeM3 = solid.Volume * 0.028316846592,
                        SurfaceAreaM2 = solid.SurfaceArea * 0.09290304,
                        FaceCount = solid.Faces.Size,
                        Faces = faceList
                    });
                }
                if (geomObj is GeometryInstance instance)
                {
                    CollectSolids(instance.GetInstanceGeometry(), solids);
                }
            }
        }

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Query Geometry";
    }
}
