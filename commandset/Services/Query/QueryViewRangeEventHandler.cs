using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Query
{
    public class QueryViewRangeEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int ViewId { get; private set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(int viewId)
        {
            ViewId = viewId;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var view = Doc.GetElement(ElementIdFactory.Create(ViewId)) as ViewPlan;
                if (view == null)
                {
                    Result = new AIResult<object> { Success = false, Message = $"View {ViewId} is not a plan view or not found" };
                    return;
                }
                var viewRange = view.GetViewRange();
                var levelIds = new[]
                {
                    new { Param = PlanViewPlane.TopClipPlane, Name = "Top" },
                    new { Param = PlanViewPlane.CutPlane, Name = "CutPlane" },
                    new { Param = PlanViewPlane.BottomClipPlane, Name = "Bottom" },
                    new { Param = PlanViewPlane.ViewDepthPlane, Name = "ViewDepth" }
                };

                var rangeData = new List<object>();
                foreach (var item in levelIds)
                {
                    var levelId = viewRange.GetLevelId(item.Param);
                    var offset = viewRange.GetOffset(item.Param);
                    var level = levelId != ElementId.InvalidElementId ? Doc.GetElement(levelId) as Level : null;
                    rangeData.Add(new
                    {
                        Parameter = item.Name,
                        LevelId = levelId.GetIntValue(),
                        LevelName = level?.Name ?? "None",
                        OffsetMm = offset * 304.8
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Response = new
                    {
                        ViewId = ViewId,
                        ViewName = view.Name,
                        ViewRange = rangeData
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

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Query View Range";
    }
}
