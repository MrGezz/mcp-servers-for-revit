using RevitMCPCommandSet.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

// This handler sits in RevitMCPCommandSet.Services.Dynamo, nested inside
// RevitMCPCommandSet.Services — which already declares its own ParameterInfo
// (AIElementFilterEventHandler.cs). C# resolves the nearer namespace first, so
// every System.Reflection.ParameterInfo below is written out in full. An alias
// would read better but would hide exactly the collision worth seeing.

namespace RevitMCPCommandSet.Services.Dynamo
{
    /// <summary>
    /// Drives the Dynamo instance hosted inside this Revit session.
    /// </summary>
    /// <remarks>
    /// EVERYTHING HERE IS REFLECTION, DELIBERATELY.
    ///
    /// This command set must build and load on a machine with no Dynamo installed
    /// at all, and must keep working across Dynamo releases that move types
    /// between assemblies. A compile-time reference to DynamoCore would make the
    /// whole command set fail to load wherever Dynamo's version did not match —
    /// taking the other 23 commands down with it — for a feature most users never
    /// touch. So nothing here is referenced; everything is discovered.
    ///
    /// WHAT IS LAZY IS THE MODEL, NOT THE ASSEMBLIES. Dynamo's assemblies are in
    /// the AppDomain from Revit startup, with nothing clicked. The DynamoModel
    /// reads null until Dynamo has been opened at least once. So "assemblies
    /// loaded, model unreachable" is the ORDINARY state before first use, not a
    /// fault, and status reports the two facts separately rather than collapsing
    /// them into one "available" flag that would be wrong half the time.
    ///
    /// The accessor is not documented anywhere. Rather than hardcode one path and
    /// fail opaquely, every candidate tried is REPORTED in the result, so a
    /// version this code has never seen produces a diagnosable answer instead of
    /// a null reference.
    /// </remarks>
    public class DynamoEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {

        /// <summary>"status", "open" or "run".</summary>
        public string Op { get; set; } = "status";

        /// <summary>Absolute path to the .dyn, for "open" and "run".</summary>
        public string GraphPath { get; set; }

        /// <summary>The result, shaped like the rest of this command set's replies.</summary>
        public Dictionary<string, object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Dynamo Operation";
        }

        public void Execute(UIApplication app)
        {
            try
            {
                switch (Op)
                {
                    case "status":
                        Result = Status();
                        break;
                    case "open":
                        Result = Open();
                        break;
                    case "run":
                        Result = Run();
                        break;
                    default:
                        Result = Fail($"Unknown op \"{Op}\". Known ops: status, open, run.");
                        break;
                }
            }
            catch (Exception ex)
            {
                // A reflective call into an unknown Dynamo build throws
                // TargetInvocationException, whose own message says nothing. The
                // inner exception is the real one.
                Exception real = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                Result = Fail($"{real.GetType().Name}: {real.Message}");
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        // -------------------------------------------------------------------
        // Result shaping
        // -------------------------------------------------------------------

        private Dictionary<string, object> Ok(string message, Dictionary<string, object> data = null)
        {
            var result = new Dictionary<string, object> { { "ok", true }, { "op", Op }, { "message", message } };
            if (data != null) foreach (var kv in data) result[kv.Key] = kv.Value;
            return result;
        }

        private Dictionary<string, object> Fail(string message, Dictionary<string, object> data = null)
        {
            var result = new Dictionary<string, object> { { "ok", false }, { "op", Op }, { "message", message } };
            if (data != null) foreach (var kv in data) result[kv.Key] = kv.Value;
            return result;
        }

        // -------------------------------------------------------------------
        // Discovery
        // -------------------------------------------------------------------

        private static IEnumerable<Assembly> DynamoAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    string name = a.GetName().Name ?? string.Empty;
                    return name.StartsWith("Dynamo", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("DynamoRevit", StringComparison.OrdinalIgnoreCase);
                });
        }

        /// <summary>
        /// Locate the live DynamoModel, reporting every accessor that was tried.
        /// </summary>
        private object FindModel(out List<string> tried)
        {
            tried = new List<string>();

            // The measured accessor on Dynamo for Revit is the static
            // RevitDynamoModel on Dynamo.Applications.DynamoRevit. Others are
            // kept as fallbacks for builds that differ; the list is ordered
            // most-likely-first and every attempt is recorded either way.
            string[] typeNames =
            {
                "Dynamo.Applications.DynamoRevit",
                "Dynamo.Applications.DynamoRevitApp",
                "Dynamo.Applications.VersionLoader",
            };
            string[] memberNames = { "RevitDynamoModel", "DynamoModel", "Model", "CurrentDynamoModel" };

            foreach (Assembly assembly in DynamoAssemblies())
            {
                foreach (string typeName in typeNames)
                {
                    Type type;
                    try { type = assembly.GetType(typeName, false); }
                    catch (Exception) { continue; }
                    if (type == null) continue;

                    foreach (string memberName in memberNames)
                    {
                        string label = $"{assembly.GetName().Name}!{typeName}.{memberName}";

                        PropertyInfo property = type.GetProperty(memberName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (property != null)
                        {
                            tried.Add(label + " (property)");
                            object value = property.GetValue(null);
                            if (LooksLikeModel(value)) return value;
                            continue;
                        }

                        FieldInfo field = type.GetField(memberName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (field != null)
                        {
                            tried.Add(label + " (field)");
                            object value = field.GetValue(null);
                            if (LooksLikeModel(value)) return value;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Duck-typing rather than a type check: the concrete DynamoModel type
        /// differs between builds, but a model always has a CurrentWorkspace.
        /// </summary>
        private static bool LooksLikeModel(object candidate)
        {
            return candidate != null && candidate.GetType().GetProperty("CurrentWorkspace") != null;
        }

        private static object CurrentWorkspace(object model)
        {
            PropertyInfo property = model.GetType().GetProperty("CurrentWorkspace");
            return property == null ? null : property.GetValue(model);
        }

        private static string WorkspaceName(object workspace)
        {
            if (workspace == null) return null;
            PropertyInfo property = workspace.GetType().GetProperty("Name");
            object value = property == null ? null : property.GetValue(workspace);
            return value == null ? null : value.ToString();
        }

        // -------------------------------------------------------------------
        // Ops
        // -------------------------------------------------------------------

        private Dictionary<string, object> Status()
        {
            List<Assembly> assemblies = DynamoAssemblies().ToList();
            List<string> tried;
            object model = FindModel(out tried);
            object workspace = model == null ? null : CurrentWorkspace(model);

            var data = new Dictionary<string, object>
            {
                { "loaded", assemblies.Count > 0 },
                { "assemblies", assemblies.Select(a => a.GetName().Name + " " + a.GetName().Version).ToList() },
                { "model_reachable", model != null },
                { "accessor", model == null ? null : model.GetType().FullName },
                { "accessors_tried", tried },
                { "current_workspace", WorkspaceName(workspace) },
            };

            if (assemblies.Count == 0)
            {
                return Fail("Dynamo assemblies are not in this Revit's AppDomain — DynamoForRevit does not appear to be installed.", data);
            }
            if (model == null)
            {
                return Ok("Dynamo is installed but its model is not reachable yet. This is the ordinary state until Dynamo has been opened once from the Manage ribbon.", data);
            }
            return Ok("Dynamo is reachable.", data);
        }

        private Dictionary<string, object> Open()
        {
            if (string.IsNullOrEmpty(GraphPath)) return Fail("open requires a path.");
            if (!File.Exists(GraphPath)) return Fail($"No file at {GraphPath}.");

            List<string> tried;
            object model = FindModel(out tried);
            if (model == null)
            {
                return Fail(
                    "Dynamo's model is not reachable, so a graph cannot be opened. Open Dynamo once from the Manage ribbon, then retry.",
                    new Dictionary<string, object> { { "accessors_tried", tried } });
            }

            // OpenFileFromPath's arity has changed between Dynamo releases, so the
            // overload is chosen by parameter count rather than assumed.
            MethodInfo open = model.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "OpenFileFromPath")
                .OrderBy(m => m.GetParameters().Length)
                .FirstOrDefault();

            if (open == null)
            {
                return Fail("This Dynamo build exposes no OpenFileFromPath method.",
                    new Dictionary<string, object> { { "model_type", model.GetType().FullName } });
            }

            System.Reflection.ParameterInfo[] parameters = open.GetParameters();
            object[] arguments = parameters.Length == 1
                ? new object[] { GraphPath }
                : new object[] { GraphPath, parameters[1].ParameterType == typeof(bool) ? (object)true : null };

            open.Invoke(model, arguments);

            object workspace = CurrentWorkspace(model);
            return Ok($"Opened {Path.GetFileName(GraphPath)}.", new Dictionary<string, object>
            {
                { "path", GraphPath },
                { "current_workspace", WorkspaceName(workspace) },
                { "overload", $"OpenFileFromPath({parameters.Length} args)" },
            });
        }

        private Dictionary<string, object> Run()
        {
            List<string> tried;
            object model = FindModel(out tried);
            if (model == null)
            {
                return Fail("Dynamo's model is not reachable, so no graph can be run.",
                    new Dictionary<string, object> { { "accessors_tried", tried } });
            }

            object workspace = CurrentWorkspace(model);
            if (workspace == null) return Fail("Dynamo has no current workspace to run.");

            // Only a home workspace runs; a custom-node workspace has no Run.
            MethodInfo run = workspace.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Run" && m.GetParameters().Length == 0);

            if (run == null)
            {
                return Fail(
                    $"The current workspace ({workspace.GetType().Name}) has no parameterless Run method. " +
                    "A custom node workspace cannot be run; open a .dyn first.",
                    new Dictionary<string, object> { { "current_workspace", WorkspaceName(workspace) } });
            }

            run.Invoke(workspace, null);

            return Ok(
                "Run requested. Dynamo evaluates asynchronously, so completion is not confirmed here — " +
                "read the graph's own outputs or the Dynamo window to see the result.",
                new Dictionary<string, object> { { "current_workspace", WorkspaceName(workspace) } });
        }
    }
}
