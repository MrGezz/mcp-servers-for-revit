using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services.Architecture;

namespace RevitMCPCommandSet.Commands.Architecture
{
    public class CreateModelCurveCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateModelCurveEventHandler _handler => (CreateModelCurveEventHandler)Handler;

        public override string CommandName => "create_model_curve";

        public CreateModelCurveCommand(UIApplication uiApp)
            : base(new CreateModelCurveEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<ModelCurveCreationInfo> data = parameters["data"].ToObject<List<ModelCurveCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "No model curve data provided");

                    _handler.SetParameters(data);

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create model curve operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create model curve: {ex.Message}");
                }
                    }
        }
    }
}
