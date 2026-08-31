using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services.Architecture;

namespace RevitMCPCommandSet.Commands.Architecture
{
    /// <summary>
    /// Command to create and place rooms in Revit
    /// </summary>
    public class CreateRoomCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateRoomEventHandler _handler => (CreateRoomEventHandler)Handler;

        /// <summary>
        /// Command name - must match the MCP tool name
        /// </summary>
        public override string CommandName => "create_room";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateRoomCommand(UIApplication uiApp)
            : base(new CreateRoomEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    List<RoomCreationInfo> data = parameters["data"].ToObject<List<RoomCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "No room data provided");

                    // Set parameters for the event handler
                    _handler.SetParameters(data);

                    // Trigger external event and wait for completion
                    if (RaiseAndWaitForCompletion(15000)) // 15 second timeout
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create room operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create room: {ex.Message}");
                }
                    }
        }
    }
}
