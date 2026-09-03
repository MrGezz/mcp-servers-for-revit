using Newtonsoft.Json;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// Service settings (the "settings" block of commandRegistry.json).
    /// </summary>
    public class ServiceSettings
    {
        /// <summary>
        /// Log level.
        /// </summary>
        [JsonProperty("logLevel")]
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// Socket service port.
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; } = 8080;

        /// <summary>
        /// Start the socket service as soon as Revit has finished starting, so an
        /// MCP client can connect without anyone clicking "Revit MCP Switch".
        /// Default on; set to false (or REVIT_MCP_AUTOSTART=0) to keep the manual
        /// switch as the only way in.
        /// </summary>
        [JsonProperty("autoStart")]
        public bool AutoStart { get; set; } = true;
    }
}
