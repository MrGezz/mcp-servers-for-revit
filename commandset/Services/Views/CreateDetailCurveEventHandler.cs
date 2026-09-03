using RevitMCPCommandSet.Utils;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateDetailCurveEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public int ViewId { get; private set; }
        public List<JObject> Lines { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        public void SetParameters(int viewId, List<JObject> lines)
        {
            ViewId = viewId;
            Lines = lines;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Create Detail Curves"))
                {
                    trans.Start();

                    View view = doc.GetElement(ElementIdFactory.Create(ViewId)) as View;
                    if (view == null)
                    {
                        Result = new AIResult<List<int>> { Success = false, Message = $"View with ID {ViewId} not found" };
                        return;
                    }

                    List<int> curveIds = new List<int>();

                    foreach (var lineObj in Lines)
                    {
                        double startX = lineObj["startX"]?.Value<double>() ?? 0;
                        double startY = lineObj["startY"]?.Value<double>() ?? 0;
                        double endX = lineObj["endX"]?.Value<double>() ?? 0;
                        double endY = lineObj["endY"]?.Value<double>() ?? 0;

                        XYZ startPt = new XYZ(startX / 304.8, startY / 304.8, 0);
                        XYZ endPt = new XYZ(endX / 304.8, endY / 304.8, 0);

                        Line line = Line.CreateBound(startPt, endPt);
                        DetailLine detailLine = doc.Create.NewDetailCurve(view, line) as DetailLine;

                        if (detailLine != null)
                        {
                            curveIds.Add(detailLine.Id.GetIntValue());
                        }
                    }

                    trans.Commit();

                    // A partial batch is still a success with a shortfall in the message;
                    // only an empty result is a failure (the contract every creator follows).
                    bool created = curveIds.Count > 0;
                    bool allSucceeded = curveIds.Count == Lines.Count;
                    Result = new AIResult<List<int>>
                    {
                        Success = created,
                        Message = !created
                            ? "Nothing was created."
                            : allSucceeded
                                ? $"Successfully created {curveIds.Count} detail curve(s)"
                                : $"Only {curveIds.Count} of {Lines.Count} detail curve(s) were created; {Lines.Count - curveIds.Count} failed silently",
                        Response = curveIds
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating detail curves: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Create Detail Curves";
    }
}
