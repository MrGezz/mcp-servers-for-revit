using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services.Architecture;

namespace RevitMCPCommandSet.Commands.Architecture
{
    public class CreateOpeningCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateOpeningEventHandler _handler => (CreateOpeningEventHandler)Handler;

        public override string CommandName => "create_opening";

        public CreateOpeningCommand(UIApplication uiApp)
            : base(new CreateOpeningEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<OpeningCreationInfo> data = parameters["data"].ToObject<List<OpeningCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "No opening data provided");

                    _handler.SetParameters(data);

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create opening operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create opening: {ex.Message}");
                }
                    }
        }
    }
}
