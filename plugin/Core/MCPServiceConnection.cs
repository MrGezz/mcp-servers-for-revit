using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// The "Revit MCP Switch" ribbon button: toggles the socket service.
    /// </summary>
    /// <remarks>
    /// Since the service auto-starts with Revit (see Application.OnApplicationInitialized),
    /// the first click most users make now STOPS it. The dialogs say so explicitly,
    /// because "Close Server" on its own reads like an error to someone who clicked
    /// the button expecting to start something.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public class MCPServiceConnection : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                SocketService service = SocketService.Instance;

                if (service.IsRunning)
                {
                    service.Stop();
                    TaskDialog.Show("Revit MCP",
                        "MCP server stopped.\n\n" +
                        "It starts automatically with Revit; click the switch again to start it now. " +
                        "To keep it off at start-up, set \"autoStart\": false in Commands\\commandRegistry.json " +
                        "or the environment variable REVIT_MCP_AUTOSTART=0.");
                }
                else
                {
                    service.Initialize(commandData.Application);
                    service.Start();
                    TaskDialog.Show("Revit MCP",
                        service.IsRunning
                            ? $"MCP server running on 127.0.0.1:{service.Port} (and ::1). AI clients can connect now."
                            : $"MCP server could not start on port {service.Port}. See the Logs folder next to the add-in for the reason (is another Revit already listening?).");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
