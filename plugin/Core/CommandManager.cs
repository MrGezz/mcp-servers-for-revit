﻿﻿using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Utils;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.IO;
using System.Reflection;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// <para>Command manager, responsible for loading and managing commands.</para>
    /// <para>Command Manager</para>
    /// </summary>
    public class CommandManager
    {
        private readonly ICommandRegistry _commandRegistry;
        private readonly ILogger _logger;
        private readonly ConfigurationManager _configManager;
        private readonly UIApplication _uiApplication;
        private readonly RevitVersionAdapter _versionAdapter;

        /// <summary>
        /// Manager in charge of loading and managing commands.
        /// </summary>
        /// <param name="commandRegistry"></param>
        /// <param name="logger"></param>
        /// <param name="configManager"></param>
        /// <param name="uiApplication"></param>
        public CommandManager(
            ICommandRegistry commandRegistry,
            ILogger logger,
            ConfigurationManager configManager,
            UIApplication uiApplication)
        {
            _commandRegistry = commandRegistry;
            _logger = logger;
            _configManager = configManager;
            _uiApplication = uiApplication;
            _versionAdapter = new RevitVersionAdapter(_uiApplication.Application);
        }

        /// <summary>
        /// <para>Load all commands specified in the configuration file.</para>
        /// <para>Load all commands specified in the configuration file.</para>
        /// </summary>
        public void LoadCommands()
        {
            _logger.Info("Start loading command.");
            string currentVersion = _versionAdapter.GetRevitVersion();
            _logger.Info("Current Revit version: {0}", currentVersion);

            // Load external commands from the configuration file.
            // Load external commands from the configuration file.
            foreach (var commandConfig in _configManager.Config.Commands)
            {
                try
                {
                    if (!commandConfig.Enabled)
                    {
                        _logger.Info("Skipping disabled command: {0}", commandConfig.CommandName);
                        continue;
                    }

                    // Check Revit version compatibility.
                    // Check Revit version compatibility.
                    if (commandConfig.SupportedRevitVersions != null &&
                        commandConfig.SupportedRevitVersions.Length > 0 &&
                        !_versionAdapter.IsVersionSupported(commandConfig.SupportedRevitVersions))
                    {
                        _logger.Warning("The command {0} is not supported by the current Revit version ({1}) and it has been skipped.",
                            commandConfig.CommandName, currentVersion);
                        continue;
                    }

                    // Replace version placeholder strings in paths.
                    // Replace version placeholder strings in paths.
                    commandConfig.AssemblyPath = commandConfig.AssemblyPath.Contains("{VERSION}")
                        ? commandConfig.AssemblyPath.Replace("{VERSION}", currentVersion)
                        : commandConfig.AssemblyPath;

                    // Load external command assembly.
                    // Load external command assembly.
                    LoadCommandFromAssembly(commandConfig);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load command {0}: {1}", commandConfig.CommandName, ex.Message);
                }
            }

            _logger.Info("Command loading complete.");
        }

        /// <summary>
        /// Loads specific commands from a specific assembly.
        /// Loads specific commands in specific assemblies.
        /// </summary>
        /// <param name="config">Configuration class describing the command.</param>
        private void LoadCommandFromAssembly(CommandConfig config)
        {
            try
            {
                // Determine the assembly path.
                // Determine the assembly path.
                string assemblyPath = config.AssemblyPath;
                if (!Path.IsPathRooted(assemblyPath))
                {
                    // If not an absolute path, resolve relative to the Commands directory.
                    // If it is not an absolute path, then it is relative to the Command's directory.
                    string baseDir = PathManager.GetCommandsDirectoryPath();
                    assemblyPath = Path.Combine(baseDir, assemblyPath);
                }

                if (!File.Exists(assemblyPath))
                {
                    _logger.Error("Command assembly does not exist: {0}", assemblyPath);
                    return;
                }

                // Load assembly.
                // Load assembly.
                Assembly assembly = Assembly.LoadFrom(assemblyPath);

                // Find types that implement the IRevitCommand interface.
                // Find types that implement the IRevitCommand interface.
                //
                // registered exists so that "the configured command was never found
                // in this assembly" is REPORTED rather than passing in silence. A
                // config entry naming a command the assembly does not contain used
                // to produce no log line at all.
                bool registered = false;

                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(RevitMCPSDK.API.Interfaces.IRevitCommand).IsAssignableFrom(type) &&
                        !type.IsInterface &&
                        !type.IsAbstract)
                    {
                        try
                        {
                            // Create a command instance.
                            // Create a command instance.
                            RevitMCPSDK.API.Interfaces.IRevitCommand command;

                            // Check whether the command implements the initializable interface.
                            // Check whether the command implements the initializable interface.
                            if (typeof(IRevitCommandInitializable).IsAssignableFrom(type))
                            {
                                // Create instance and initialize.
                                // Create instance and initialize.
                                command = (IRevitCommand)Activator.CreateInstance(type);
                                ((IRevitCommandInitializable)command).Initialize(_uiApplication);
                            }
                            else
                            {
                                // Try searching for constructors that accept UIApplication.
                                // Try searching for constructors that accept UIApplication.
                                var constructor = type.GetConstructor(new[] { typeof(UIApplication) });
                                if (constructor != null)
                                {
                                    command = (IRevitCommand)constructor.Invoke(new object[] { _uiApplication });
                                }
                                else
                                {
                                    // Use a parameterless constructor.
                                    // Use a parameterless constructor.
                                    command = (IRevitCommand)Activator.CreateInstance(type);
                                }
                            }

                            // Check whether the command name matches the configuration.
                            // Check whether the command name matches the configuration.
                            if (command.CommandName == config.CommandName)
                            {
                                _commandRegistry.RegisterCommand(command);
                                registered = true;
                                _logger.Info("Registered command [{0}] from assembly {1}",
                                    command.CommandName, Path.GetFileName(assemblyPath));
                                break; // Exit the loop after finding a matching command.
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Failed to create command instance for type [{0}]: {1}", type.FullName, ex.Message);
                        }
                    }
                }

                if (!registered)
                {
                    _logger.Warning("Configured command [{0}] was not found in assembly {1}",
                        config.CommandName, Path.GetFileName(assemblyPath));
                }
            }
            catch (ReflectionTypeLoadException rtle)
            {
                // ReflectionTypeLoadException.Message is always the same
                // uninformative sentence; the reason a type could not be loaded —
                // the missing or mismatched dependency — lives only in
                // LoaderExceptions. Reporting just the Message is what makes a
                // genuine dependency conflict indistinguishable from every other
                // load failure.
                _logger.Error("Failed to load types from command assembly: {0}", rtle.Message);

                if (rtle.LoaderExceptions != null)
                {
                    foreach (var loaderException in rtle.LoaderExceptions)
                    {
                        if (loaderException != null)
                        {
                            _logger.Error("  Dependency load failure: {0}", loaderException.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load command assembly: {0}", ex.Message);
            }
        }
    }
}
