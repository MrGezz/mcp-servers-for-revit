using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class SetViewRangeCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private SetViewRangeEventHandler _handler => (SetViewRangeEventHandler)Handler;

        public override string CommandName => "set_view_range";

        public SetViewRangeCommand(UIApplication uiApp)
            : base(new SetViewRangeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"]?.Value<int>() ?? 0;
                    double topOffset = parameters["topOffset"]?.Value<double>() ?? 0;
                    double cutOffset = parameters["cutOffset"]?.Value<double>() ?? 1200;
                    double bottomOffset = parameters["bottomOffset"]?.Value<double>() ?? 0;
                    double viewDepthOffset = parameters["viewDepthOffset"]?.Value<double>() ?? 0;
                    int? topLevelId = parameters["topLevelId"]?.Value<int>();

                    _handler.SetParameters(viewId, topOffset, cutOffset, bottomOffset, viewDepthOffset, topLevelId);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Set view range operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set view range: {ex.Message}");
                }
                    }
        }
    }
}
