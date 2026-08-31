using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateDetailCurveCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateDetailCurveEventHandler _handler => (CreateDetailCurveEventHandler)Handler;

        public override string CommandName => "create_detail_curve";

        public CreateDetailCurveCommand(UIApplication uiApp)
            : base(new CreateDetailCurveEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"]?.Value<int>() ?? 0;
                    JArray linesArray = parameters["lines"] as JArray;

                    List<JObject> lines = new List<JObject>();
                    if (linesArray != null)
                    {
                        foreach (var item in linesArray)
                        {
                            lines.Add(item as JObject);
                        }
                    }

                    _handler.SetParameters(viewId, lines);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create detail curve operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create detail curve: {ex.Message}");
                }
                    }
        }
    }
}
