using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class ManageScheduleFieldsCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private ManageScheduleFieldsEventHandler _handler => (ManageScheduleFieldsEventHandler)Handler;

        public override string CommandName => "manage_schedule_fields";

        public ManageScheduleFieldsCommand(UIApplication uiApp)
            : base(new ManageScheduleFieldsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int scheduleId = parameters["scheduleId"]?.Value<int>() ?? 0;
                    string action = parameters["action"]?.Value<string>() ?? "add";
                    string fieldName = parameters["fieldName"]?.Value<string>();
                    int? position = parameters["position"]?.Value<int>();

                    _handler.SetParameters(scheduleId, action, fieldName, position);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Manage schedule fields operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to manage schedule fields: {ex.Message}");
                }
                    }
        }
    }
}
