using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateViewTemplateCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateViewTemplateEventHandler _handler => (CreateViewTemplateEventHandler)Handler;

        public override string CommandName => "create_view_template";

        public CreateViewTemplateCommand(UIApplication uiApp)
            : base(new CreateViewTemplateEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int sourceViewId = parameters["sourceViewId"]?.Value<int>() ?? 0;
                    string name = parameters["name"]?.Value<string>();

                    _handler.SetParameters(sourceViewId, name);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create view template operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create view template: {ex.Message}");
                }
                    }
        }
    }
}
