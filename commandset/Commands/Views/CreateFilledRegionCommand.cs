using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateFilledRegionCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateFilledRegionEventHandler _handler => (CreateFilledRegionEventHandler)Handler;

        public override string CommandName => "create_filled_region";

        public CreateFilledRegionCommand(UIApplication uiApp)
            : base(new CreateFilledRegionEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"]?.Value<int>() ?? 0;
                    string filledRegionTypeName = parameters["filledRegionTypeName"]?.Value<string>();
                    JArray boundaryArray = parameters["boundary"] as JArray;

                    List<List<JObject>> boundary = new List<List<JObject>>();
                    if (boundaryArray != null)
                    {
                        foreach (var loop in boundaryArray)
                        {
                            JArray loopArray = loop as JArray;
                            if (loopArray != null)
                            {
                                List<JObject> points = new List<JObject>();
                                foreach (var pt in loopArray)
                                {
                                    points.Add(pt as JObject);
                                }
                                boundary.Add(points);
                            }
                        }
                    }

                    _handler.SetParameters(viewId, boundary, filledRegionTypeName);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create filled region operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create filled region: {ex.Message}");
                }
                    }
        }
    }
}
