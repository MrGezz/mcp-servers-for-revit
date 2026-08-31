using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class SetViewPropertiesCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private SetViewPropertiesEventHandler _handler => (SetViewPropertiesEventHandler)Handler;

        public override string CommandName => "set_view_properties";

        public SetViewPropertiesCommand(UIApplication uiApp)
            : base(new SetViewPropertiesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"]?.Value<int>() ?? 0;
                    JObject properties = parameters["properties"] as JObject;

                    _handler.SetParameters(viewId, properties);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Set view properties operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set view properties: {ex.Message}");
                }
                    }
        }
    }
}
