using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class SetParametersCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private SetParametersEventHandler _handler => (SetParametersEventHandler)Handler;
        public override string CommandName => "set_parameters";
        public SetParametersCommand(UIApplication uiApp)
            : base(new SetParametersEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int elementId = parameters["elementId"].Value<int>();
                    var paramValues = parameters["parameters"] as JObject;
                    if (paramValues == null)
                        throw new ArgumentException("parameters object is required");
                    _handler.SetParameters(elementId, paramValues);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Set parameters timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set parameters: {ex.Message}");
                }
                    }
        }
    }
}
