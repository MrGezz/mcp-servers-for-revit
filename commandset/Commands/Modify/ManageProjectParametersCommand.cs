using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class ManageProjectParametersCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private ManageProjectParametersEventHandler _handler => (ManageProjectParametersEventHandler)Handler;
        public override string CommandName => "manage_project_parameters";
        public ManageProjectParametersCommand(UIApplication uiApp)
            : base(new ManageProjectParametersEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string action = parameters["action"].Value<string>();
                    string sharedParamFile = parameters["sharedParamFile"]?.Value<string>();
                    string paramGroup = parameters["paramGroup"]?.Value<string>();
                    var paramList = parameters["params"] as JArray;
                    _handler.SetParameters(action, sharedParamFile, paramGroup, paramList);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Manage project parameters timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to manage project parameters: {ex.Message}");
                }
                    }
        }
    }
}
