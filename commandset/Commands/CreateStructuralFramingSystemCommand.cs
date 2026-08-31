using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Structure;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateStructuralFramingSystemCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateStructuralFramingSystemEventHandler _handler => (CreateStructuralFramingSystemEventHandler)Handler;

        /// <summary>
        /// Command name for MCP protocol
        /// </summary>
        public override string CommandName => "create_structural_framing_system";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateStructuralFramingSystemCommand(UIApplication uiApp)
            : base(new CreateStructuralFramingSystemEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    StructuralFramingSystemCreationInfo data = parameters.ToObject<StructuralFramingSystemCreationInfo>();

                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Beam system creation data is null");

                    // Validate before processing
                    if (!data.Validate(out string validationError))
                    {
                        throw new ArgumentException($"Invalid beam system parameters: {validationError}");
                    }

                    // Set parameters and trigger event
                    _handler.SetParameters(data);

                    // Wait for completion with 15 second timeout (beam creation can be slower)
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Beam system creation operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create structural framing system: {ex.Message}");
                }
                    }
        }
    }
}
