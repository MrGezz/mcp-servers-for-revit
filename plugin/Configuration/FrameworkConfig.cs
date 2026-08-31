using Newtonsoft.Json;
using System.Collections.Generic;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// Framework configuration class.
    /// </summary>
    public class FrameworkConfig
    {
        /// <summary>
        /// Command configuration list.
        /// </summary>
        [JsonProperty("commands")]
        public List<CommandConfig> Commands { get; set; } = new List<CommandConfig>();

        /// <summary>
        /// Global settings.
        /// </summary>
        [JsonProperty("settings")]
        public ServiceSettings Settings { get; set; } = new ServiceSettings();
    }
}
