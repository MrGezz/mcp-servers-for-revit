using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace revit_mcp_plugin.Utils
{
    public static class PathManager
    {
        /// <summary>
        /// Gets the root application data directory
        /// </summary>
        public static string GetAppDataDirectoryPath()
        {
            string applicationPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string applicationDirectory = Path.GetDirectoryName(applicationPath);

            return applicationDirectory;
        }
        /// <summary>
        /// Gets the path to the Commands directory
        /// </summary>
        public static string GetCommandsDirectoryPath()
        {
            string appDataDirectory = GetAppDataDirectoryPath();
            string commandsDirectory = Path.Combine(appDataDirectory, "Commands");

            EnsureDirectoryExists(commandsDirectory);

            return commandsDirectory;
        }
        /// <summary>
        /// Gets the path to the Logs directory
        /// </summary>
        public static string GetLogsDirectoryPath()
        {
            string appDataDirectory = GetAppDataDirectoryPath();
            string logsDirectory = Path.Combine(appDataDirectory, "Logs");

            EnsureDirectoryExists(logsDirectory);

            return logsDirectory;
        }
        /// <summary>
        /// Gets the path to the command registry file.
        /// If the file doesn't exist, creates it with default content.
        /// </summary>
        /// <param name="createIfNotExists">Whether to create a default file if it doesn't exist (default: true)</param>
        /// <returns>Path to the command registry file</returns>
        public static string GetCommandRegistryFilePath(bool createIfNotExists = true)
        {
            string commandsDirectory = GetCommandsDirectoryPath();
            string registryFilePath = Path.Combine(commandsDirectory, "commandRegistry.json");

            if (createIfNotExists && !File.Exists(registryFilePath))
            {
                CreateDefaultCommandRegistryFile(registryFilePath);
            }

            return registryFilePath;
        }
        /// <summary>
        /// Creates the command registry by SCANNING the deployed command sets, rather
        /// than writing an empty list.
        ///
        /// The previous default was { "commands": [] }. Because this method is reached
        /// from the READ path (GetCommandRegistryFilePath defaults to createIfNotExists
        /// = true, and ConfigurationManager calls it in its constructor), a fresh
        /// install manufactured that empty file, loaded it SUCCESSFULLY with zero
        /// commands, and thereby made the "No configuration file found" diagnostic
        /// unreachable. Every tool then answered "Method not found" while the bridge
        /// reported itself healthy. The only code that populated the registry was a
        /// Settings page button that nothing documents.
        ///
        /// Commands are seeded ENABLED: a user who installed the add-in wants it to
        /// work, and the Settings page remains the place to turn individual ones off.
        /// </summary>
        private static void CreateDefaultCommandRegistryFile(string filePath)
        {
            var problems = new List<string>();
            var seeded = ScanDeployedCommands(problems);

            try
            {
                var registry = new { commands = seeded };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(registry, Formatting.Indented));
            }
            catch (Exception ex)
            {
                new Logger().Error("Could not write the command registry: {0}", ex.Message);
                return;
            }

            if (seeded.Count == 0)
            {
                new Logger().Error(
                    "Command registry created with NO commands. The bridge will answer on its " +
                    "socket and reject every tool call. Expected to find command sets under: " +
                    GetCommandsDirectoryPath() +
                    (problems.Count > 0 ? " Problems: " + string.Join("; ", problems) : string.Empty));
            }
            else
            {
                new Logger().Info("Command registry seeded with {0} command(s) from the deployed command sets.", seeded.Count);
            }
        }

        /// <summary>
        /// Every command the deployed command sets actually provide, as registry entries.
        /// Shared by the first-run seeder and the upgrade reconciler so the two can never
        /// disagree about what "deployed" means.
        /// </summary>
        public static List<CommandRegistryEntry> ScanDeployedCommands(List<string> problems)
        {
            var seeded = new List<CommandRegistryEntry>();
            problems = problems ?? new List<string>();

            try
            {
                string commandsDirectory = GetCommandsDirectoryPath();
                foreach (string setDirectory in Directory.GetDirectories(commandsDirectory))
                {
                    string setName = Path.GetFileName(setDirectory);
                    if (setName.StartsWith(".")) continue;

                    string commandJsonPath = Path.Combine(setDirectory, "command.json");
                    if (!File.Exists(commandJsonPath)) continue;

                    JObject setData;
                    try
                    {
                        setData = JObject.Parse(File.ReadAllText(commandJsonPath));
                    }
                    catch (Exception ex)
                    {
                        problems.Add(setName + "/command.json is not valid JSON: " + ex.Message);
                        continue;
                    }

                    // Version subfolders are the years this set actually shipped a DLL for.
                    var versions = Directory.GetDirectories(setDirectory)
                        .Select(Path.GetFileName)
                        .Where(n => int.TryParse(n, out _))
                        .ToList();
                    if (versions.Count == 0)
                    {
                        problems.Add(setName + " has a command.json but no version subfolder, so no DLL can be resolved");
                        continue;
                    }

                    JArray commands = setData["commands"] as JArray;
                    if (commands == null) continue;

                    foreach (JToken command in commands)
                    {
                        string commandName = (string)command["commandName"];
                        string assemblyPath = (string)command["assemblyPath"];
                        if (string.IsNullOrWhiteSpace(commandName)) continue;

                        // Only claim the versions whose DLL is actually on disk.
                        var supported = new List<string>();
                        string template = null;
                        foreach (string version in versions)
                        {
                            string versionDir = Path.Combine(setDirectory, version);
                            string dll = string.IsNullOrWhiteSpace(assemblyPath)
                                ? Directory.GetFiles(versionDir, "*.dll").FirstOrDefault()
                                : Path.Combine(versionDir, assemblyPath);
                            if (dll == null || !File.Exists(dll)) continue;
                            supported.Add(version);
                            if (template == null)
                            {
                                template = Path.Combine(setName, "{VERSION}", Path.GetFileName(dll));
                            }
                        }

                        if (supported.Count == 0 || template == null) continue;

                        seeded.Add(new CommandRegistryEntry
                        {
                            CommandName = commandName,
                            AssemblyPath = template,
                            Enabled = true,
                            SupportedRevitVersions = supported.ToArray(),
                            Description = (string)command["description"]
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                problems.Add("scan failed: " + ex.Message);
            }

            return seeded;
        }

        /// <summary>
        /// Add commands that are DEPLOYED but not yet in the registry, and report how many.
        ///
        /// Seeding alone is not enough: it only runs when the registry is absent, so an
        /// existing install that is upgraded keeps its old registry and every newly added
        /// command is unreachable while the bridge reports itself healthy. Measured: 86
        /// commands deployed, 24 in the registry, and the new ones answering
        /// "Method not found".
        ///
        /// Existing entries are left EXACTLY as they are, so a command a user deliberately
        /// disabled stays disabled across an upgrade.
        /// </summary>
        public static int ReconcileCommandRegistry(out List<string> added)
        {
            added = new List<string>();
            string registryPath = GetCommandRegistryFilePath();
            if (!File.Exists(registryPath)) return 0;

            try
            {
                JObject registry = JObject.Parse(File.ReadAllText(registryPath));
                // The file has shipped with both "commands" and "Commands"; honour whichever
                // is present rather than silently starting a second list beside it.
                string key = registry.Property("commands") != null ? "commands"
                           : registry.Property("Commands") != null ? "Commands"
                           : "commands";
                JArray existing = registry[key] as JArray ?? new JArray();

                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JToken t in existing)
                {
                    string n = (string)t["commandName"];
                    if (!string.IsNullOrWhiteSpace(n)) known.Add(n);
                }

                var problems = new List<string>();
                foreach (CommandRegistryEntry entry in ScanDeployedCommands(problems))
                {
                    if (known.Contains(entry.CommandName)) continue;
                    existing.Add(JObject.FromObject(new
                    {
                        commandName = entry.CommandName,
                        assemblyPath = entry.AssemblyPath,
                        enabled = entry.Enabled,
                        supportedRevitVersions = entry.SupportedRevitVersions,
                        description = entry.Description
                    }));
                    added.Add(entry.CommandName);
                }

                if (added.Count == 0) return 0;

                registry[key] = existing;
                File.WriteAllText(registryPath, JsonConvert.SerializeObject(registry, Formatting.Indented));
                return added.Count;
            }
            catch (Exception ex)
            {
                new Logger().Error("Could not reconcile the command registry: {0}", ex.Message);
                return 0;
            }
        }
        /// <summary>
        /// Ensures that the specified directory exists
        /// </summary>
        /// <param name="directoryPath">The path to check and create if needed</param>
        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }

    /// <summary>One command as the registry stores it.</summary>
    public class CommandRegistryEntry
    {
        public string CommandName { get; set; }
        public string AssemblyPath { get; set; }
        public bool Enabled { get; set; }
        public string[] SupportedRevitVersions { get; set; }
        public string Description { get; set; }
    }
}
