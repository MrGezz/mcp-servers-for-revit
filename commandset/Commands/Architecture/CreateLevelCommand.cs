using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Architecture;
using LevelCreationInfo = RevitMCPCommandSet.Models.Architecture.LevelInfo;

namespace RevitMCPCommandSet.Commands.Architecture
{
    /// <summary>
    /// Command to create levels in Revit with automatic floor plan view generation
    /// </summary>
    public class CreateLevelCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateLevelEventHandler _handler => (CreateLevelEventHandler)Handler;

        /// <summary>
        /// Command name - must match the MCP tool name
        /// </summary>
        public override string CommandName => "create_level";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateLevelCommand(UIApplication uiApp)
            : base(new CreateLevelEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    List<LevelCreationInfo> data = parameters["data"].ToObject<List<LevelCreationInfo>>();
                    if (data == null || data.Count == 0)
                        throw new ArgumentNullException(nameof(data), "No level data provided");

                    // Set parameters for the event handler
                    _handler.SetParameters(data);

                    // Trigger external event and wait for completion
                    if (RaiseAndWaitForCompletion(15000)) // 15 second timeout
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create level operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create level: {ex.Message}");
                }
                    }
        }
    }
}
