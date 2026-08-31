using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateScheduleCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateScheduleEventHandler _handler => (CreateScheduleEventHandler)Handler;

        public override string CommandName => "create_schedule";

        public CreateScheduleCommand(UIApplication uiApp)
            : base(new CreateScheduleEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<ScheduleCreationInfo> data = new List<ScheduleCreationInfo>();
                    data = parameters["data"].ToObject<List<ScheduleCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Input data from AI is null");

                    _handler.SetParameters(data);

                    if (RaiseAndWaitForCompletion(15000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create schedule operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create schedule: {ex.Message}");
                }
                    }
        }
    }
}
