using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Annotation;

namespace RevitMCPCommandSet.Commands.Annotation
{
    public class CreateRevisionCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateRevisionEventHandler _handler => (CreateRevisionEventHandler)Handler;

        public override string CommandName => "create_revision";

        public CreateRevisionCommand(UIApplication uiApp)
            : base(new CreateRevisionEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string name = parameters["name"]?.Value<string>();
                    string date = parameters["date"]?.Value<string>();
                    string number = parameters["number"]?.Value<string>();
                    string description = parameters["description"]?.Value<string>();

                    _handler.SetParameters(name, date, number, description);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create revision operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create revision: {ex.Message}");
                }
                    }
        }
    }
}
