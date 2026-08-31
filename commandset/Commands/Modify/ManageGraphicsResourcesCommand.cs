using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class ManageGraphicsResourcesCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private ManageGraphicsResourcesEventHandler _handler => (ManageGraphicsResourcesEventHandler)Handler;

        public override string CommandName => "manage_graphics_resources";

        public ManageGraphicsResourcesCommand(UIApplication uiApp)
            : base(new ManageGraphicsResourcesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string action = parameters["action"]?.Value<string>() ?? "line_style";
                    string name = parameters["name"]?.Value<string>();
                    JObject properties = parameters["properties"] as JObject;

                    _handler.SetParameters(action, name, properties);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Manage graphics resources operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to manage graphics resources: {ex.Message}");
                }
                    }
        }
    }
}
