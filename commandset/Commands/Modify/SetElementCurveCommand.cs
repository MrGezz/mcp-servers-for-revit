using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class SetElementCurveCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private SetElementCurveEventHandler _handler => (SetElementCurveEventHandler)Handler;
        public override string CommandName => "set_element_curve";
        public SetElementCurveCommand(UIApplication uiApp)
            : base(new SetElementCurveEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int elementId = parameters["elementId"].Value<int>();
                    var startPoint = parameters["startPoint"] as JObject;
                    var endPoint = parameters["endPoint"] as JObject;
                    if (startPoint == null || endPoint == null)
                        throw new ArgumentException("startPoint and endPoint are required");
                    _handler.SetParameters(elementId, startPoint, endPoint);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Set element curve timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set element curve: {ex.Message}");
                }
                    }
        }
    }
}
