using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ExportRoomDataCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private ExportRoomDataEventHandler _handler => (ExportRoomDataEventHandler)Handler;

        public override string CommandName => "export_room_data";

        public ExportRoomDataCommand(UIApplication uiApp)
            : base(new ExportRoomDataEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse optional parameters
                    bool includeUnplacedRooms = parameters?["includeUnplacedRooms"]?.Value<bool>() ?? false;
                    bool includeNotEnclosedRooms = parameters?["includeNotEnclosedRooms"]?.Value<bool>() ?? false;

                    // Set parameters
                    _handler.SetParameters(includeUnplacedRooms, includeNotEnclosedRooms);

                    // Execute and wait
                    if (RaiseAndWaitForCompletion(60000)) // 60 second timeout
                    {
                        return _handler.ResultInfo;
                    }
                    else
                    {
                        throw new TimeoutException("Export room data operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to export room data: {ex.Message}");
                }
                    }
        }
    }
}
