using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class ManageFamilyParametersCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private ManageFamilyParametersEventHandler _handler => (ManageFamilyParametersEventHandler)Handler;
        public override string CommandName => "manage_family_parameters";
        public ManageFamilyParametersCommand(UIApplication uiApp)
            : base(new ManageFamilyParametersEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string action = parameters["action"].Value<string>();
                    int familyId = parameters["familyId"].Value<int>();
                    string name = parameters["name"]?.Value<string>();
                    string newName = parameters["newName"]?.Value<string>();
                    string formula = parameters["formula"]?.Value<string>();
                    string paramType = parameters["type"]?.Value<string>();
                    _handler.SetParameters(action, familyId, name, newName, formula, paramType);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Manage family parameters timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to manage family parameters: {ex.Message}");
                }
                    }
        }
    }
}
