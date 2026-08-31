using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;

namespace revit_mcp_plugin.Configuration
{
    public class ConfigurationManager
    {
        private readonly ILogger _logger;
        private readonly string _configPath;

        public FrameworkConfig Config { get; private set; }

        public ConfigurationManager(ILogger logger)
        {
            _logger = logger;

            // Configuration file path.
            _configPath = PathManager.GetCommandRegistryFilePath();
        }

        /// <summary>
        /// Load configuration from a JSON file.
        /// </summary>
        public void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    // Pick up commands added by an upgrade BEFORE reading, or every one of
                    // them answers "Method not found" while the bridge looks healthy.
                    List<string> newlyRegistered;
                    int addedCount = PathManager.ReconcileCommandRegistry(out newlyRegistered);
                    if (addedCount > 0)
                    {
                        _logger.Info(
                            "Registered {0} newly deployed command(s): {1}",
                            addedCount,
                            string.Join(", ", newlyRegistered));
                    }

                    string json = File.ReadAllText(_configPath);
                    Config = JsonConvert.DeserializeObject<FrameworkConfig>(json);

                    // "Loaded" and "loaded something usable" are different claims. A registry
                    // holding zero commands parses perfectly and leaves every tool call
                    // answering "Method not found" while the log says the file loaded fine.
                    // That state cost two people days of diagnosis, so it gets its own line.
                    int commandCount = Config?.Commands?.Count ?? 0;
                    if (commandCount == 0)
                    {
                        _logger.Error(
                            "Configuration file loaded from {0} but it contains NO commands. " +
                            "The server will accept connections and reject every command. " +
                            "Open Settings and save the command set, then toggle the Revit MCP " +
                            "Switch off and on so the commands bind.",
                            _configPath);
                    }
                    else
                    {
                        _logger.Info("Configuration file loaded: {0} ({1} command(s))", _configPath, commandCount);
                    }
                }
                else
                {
                    _logger.Error("No configuration file found at {0}.", _configPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load configuration file: {0}", ex.Message);
            }

            // Register load time.
            _lastConfigLoadTime = DateTime.Now;
        }

        ///// <summary>
        ///// <para>Reload configuration.</para>
        ///  <para>Reload configuration.</para>
        ///// </summary>
        //public void RefreshConfiguration()
        //{
        //    LoadConfiguration();
        //    _logger.Info("Configuration has been reloaded.");
        //}

        //public bool HasConfigChanged()
        //{
        //    if (!File.Exists(_configPath))
        //        return false;

        //    DateTime lastWrite = File.GetLastWriteTime(_configPath);
        //    return lastWrite > _lastConfigLoadTime;
        //}

        private DateTime _lastConfigLoadTime;
    }
}
