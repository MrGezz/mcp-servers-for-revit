using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// External event handler that compiles and executes dynamic C# code in Revit.
    /// </summary>
    public class ExecuteCodeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public const string TransactionModeAuto = "auto";
        public const string TransactionModeNone = "none";

        static ExecuteCodeEventHandler()
        {
            // Fix contributed as upstream PR #46 by @KennanChan. Roslyn requests facade
            // assemblies at versions Revit has not loaded, and the CLR will not substitute
            // unaided, so the first compile throws FileNotFoundException naming a version
            // nobody shipped. Reimplemented here rather than merged; the work is theirs.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;
        }

        /// <summary>
        /// Resolve a Roslyn dependency by SIMPLE NAME against what is already loaded.
        /// </summary>
        /// <remarks>
        /// Microsoft.CodeAnalysis pulls in facade assemblies —
        /// System.Runtime.CompilerServices.Unsafe, System.Collections.Immutable and
        /// friends — and asks for specific versions of them. Revit has usually already
        /// loaded a different version of the same assembly, and the CLR does not
        /// substitute one for the other on its own: the first attempt to compile throws
        /// FileNotFoundException naming a version nobody shipped. Binding by simple name
        /// is what the CLR itself would do given a publisher policy, and is safe here
        /// because these are strictly additive facades.
        ///
        /// Returning null is not a failure path to hide — it hands the CLR back its own
        /// original error rather than masking it with ours.
        /// </remarks>
        private static Assembly ResolveDependency(object sender, ResolveEventArgs args)
        {
            AssemblyName requested;
            try { requested = new AssemblyName(args.Name); }
            catch (Exception) { return null; }

            Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => !a.IsDynamic &&
                                     string.Equals(a.GetName().Name, requested.Name,
                                                   StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;

            try
            {
                string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(here))
                {
                    string candidate = Path.Combine(here, requested.Name + ".dll");
                    if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                }
            }
            catch (Exception)
            {
                // Best effort only. Fall through and let the CLR report the real failure.
            }

            return null;
        }

        // Code execution parameters
        private string _generatedCode;
        private object[] _executionParameters;
        private string _transactionMode = TransactionModeAuto;

        // Execution result
        public ExecutionResultInfo ResultInfo { get; private set; }

        // State synchronization
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Set the code and parameters to execute
        public void SetExecutionParameters(string code, object[] parameters = null, string transactionMode = TransactionModeAuto)
        {
            _generatedCode = code;
            _executionParameters = parameters ?? Array.Empty<object>();
            _transactionMode = transactionMode == TransactionModeNone ? TransactionModeNone : TransactionModeAuto;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Wait for execution to finish — IWaitableExternalEventHandler implementation
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                ResultInfo = new ExecutionResultInfo();

                object result;
                if (_transactionMode == TransactionModeNone)
                {
                    result = CompileAndExecuteCode(
                        code: _generatedCode,
                        doc: doc,
                        parameters: _executionParameters
                    );
                }
                else
                {
                    using (var transaction = new Transaction(doc, "Execute AI Code"))
                    {
                        transaction.Start();

                        result = CompileAndExecuteCode(
                            code: _generatedCode,
                            doc: doc,
                            parameters: _executionParameters
                        );

                        transaction.Commit();
                    }
                }

                ResultInfo.Success = true;
                ResultInfo.Result = JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                ResultInfo.Success = false;
                ResultInfo.ErrorMessage = $"Execution failed: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private object CompileAndExecuteCode(string code, Document doc, object[] parameters)
        {
            // Wrap submitted code in a fixed entry-point scaffold
            var wrappedCode = $@"
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace AIGeneratedCode
{{
    public static class CodeExecutor
    {{
        public static object Execute(Document document, object[] parameters)
        {{
            // User code entry point
            {code}
        }}
    }}
}}";

            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

            // Fix contributed as upstream PR #26 by @Avinashhv. Where another add-in ships
            // its own copies of Autodesk SDK DLLs, GetAssemblies() returns two assemblies
            // with the same simple name; handing both to CSharpCompilation produces Line 0
            // fatal errors that block ALL compilation. Reimplemented here rather than
            // merged; the work is theirs.
            // Add all loaded assemblies as references, deduplicated by simple name
            // Deduplicate by simple name before handing anything to Roslyn.
            //
            // Installs that include the BIM360 add-in load a second copy of several
            // Autodesk SDK assemblies (Autodesk.JsonApi, Autodesk.Http, ...) under the
            // same simple name as Revit's own. Passing both to CSharpCompilation raises
            // "An assembly with the same simple name has already been imported" as a
            // Line 0 error, which blocks compilation regardless of the submitted code —
            // send_code_to_revit is unusable on those machines.
            //
            // First one loaded wins, which is Revit's own copy, and matches how the CLR
            // resolves the binding anyway.
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .GroupBy(a => a.GetName().Name)
                .Select(g => g.First())
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            // Compile the code
            var compilation = CSharpCompilation.Create(
                "AIGeneratedCode",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                // Handle compilation result
                if (!result.Success)
                {
                    var errors = string.Join("\n", result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line}: {d.GetMessage()}"));
                    throw new Exception($"Code compilation error:\n{errors}");
                }

                // Invoke the execute method via reflection
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                var executorType = assembly.GetType("AIGeneratedCode.CodeExecutor");
                var executeMethod = executorType.GetMethod("Execute");

                return executeMethod.Invoke(null, new object[] { doc, parameters });
            }
        }

        public string GetName()
        {
            return "Execute AI Code";
        }
    }

    // Execution result data structure
    public class ExecutionResultInfo
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
