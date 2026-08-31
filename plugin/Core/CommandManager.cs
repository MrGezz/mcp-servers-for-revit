using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Utils;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
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

        // Directories to probe when a command assembly's dependency cannot be bound.
        // Static because the AssemblyResolve event is per-AppDomain: one resolver
        // serves every CommandManager instance, and registering it more than once
        // would run the same probe repeatedly for a single failed bind.
        private static readonly HashSet<string> _probeDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _probeGate = new object();
        private static bool _resolverInstalled;

        /// <summary>
        /// Remember a directory to search for dependencies, and make sure the
        /// AppDomain resolver that searches it is installed.
        /// </summary>
        private static void RegisterProbeDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;

            lock (_probeGate)
            {
                _probeDirectories.Add(directory);
                if (_resolverInstalled) return;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromProbeDirectories;
                _resolverInstalled = true;
            }
        }

        /// <summary>
        /// Bind a dependency of a byte-loaded command assembly from the directory it
        /// shipped in. Returns null when nothing matches, which lets the normal
        /// binding failure surface unchanged - this resolver only ADDS candidates,
        /// it never masks a genuine missing-assembly error.
        /// </summary>
        private static Assembly ResolveFromProbeDirectories(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName)) return null;

            // An assembly already in the AppDomain is the correct answer: returning it
            // avoids loading a SECOND copy of the same identity, which produces the
            // "type X is not type X" class of error that is very hard to read.
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }

            string[] directories;
            lock (_probeGate) { directories = new string[_probeDirectories.Count]; _probeDirectories.CopyTo(directories); }

            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory, simpleName + ".dll");
                if (!File.Exists(candidate)) continue;
                try
                {
                    // Byte-load here too, for the same unlocking reason as the caller.
                    return Assembly.Load(File.ReadAllBytes(candidate));
                }
                catch (Exception ex)
                {
                    // A candidate that will not load is not this resolver's problem to
                    // REPORT - keep probing, and let the binder raise the real error if
                    // nothing satisfies the request. But it is not nothing either: a
                    // dependency that exists on disk and refuses to load is exactly the
                    // detail someone debugging a bind failure needs, so it is traced
                    // rather than swallowed. Debug.WriteLine, not the logger, because
                    // this runs on an AppDomain callback that may fire during shutdown.
                    System.Diagnostics.Debug.WriteLine(
                        "AssemblyResolve: candidate '" + candidate + "' failed to load: " + ex.Message);
                }
            }

            return null;
        }

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

                // P1-7. Assembly.LoadFrom() held an open file handle on the DLL for
                // the life of the AppDomain, so updating the add-in required a full
                // Revit restart. Reading the bytes and calling Assembly.Load releases
                // the handle immediately.
                //
                // THE TRAP THAT OPENS, AND WHY THE RESOLVER BELOW IS NOT OPTIONAL.
                // LoadFrom places the assembly in the load-FROM context, which probes
                // the assembly's OWN directory when binding its dependencies.
                // Assembly.Load(byte[]) loads into the default context, which does
                // not. Eight assemblies ship beside RevitMCPCommandSet.dll
                // (Newtonsoft.Json, two Roslyn assemblies, two Nice3point assemblies,
                // RevitMCPSDK, WinRT.Runtime, Microsoft.Windows.SDK.NET). Without the
                // resolver each of them would fail to bind at FIRST USE - a crash
                // during command execution, far away from the load that caused it.
                RegisterProbeDirectory(Path.GetDirectoryName(assemblyPath));
                Assembly assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));

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
