using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Memory;

namespace RevitMCPCommandSet.Commands.Memory
{
    /// <summary>
    /// One command for every project-memory operation. A single dispatching command
    /// keeps the registry small and means the graph has exactly one entry point into
    /// the document.
    /// </summary>
    public class ProjectMemoryCommand : ExternalEventCommandBase
    {
        private ProjectMemoryEventHandler _handler => (ProjectMemoryEventHandler)Handler;

        public override string CommandName => "project_memory_op";

        public ProjectMemoryCommand(UIApplication uiApp)
            : base(new ProjectMemoryEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                string action = parameters?["action"]?.ToString();
                if (string.IsNullOrWhiteSpace(action))
                {
                    throw new ArgumentException(
                        "An 'action' is required: read, query, write, stats, raw or clear.");
                }

                JObject payload = parameters["payload"] as JObject ?? new JObject();
                _handler.SetParameters(action, payload);

                if (RaiseAndWaitForCompletion(15000))
                    return _handler.Result;

                throw new TimeoutException(
                    "Project memory operation timed out after 15s. If a modal dialog is open in " +
                    "Revit, the external event queue is blocked until it is dismissed.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Project memory operation failed: {ex.Message}");
            }
        }
    }
}
