using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateGridCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateGridEventHandler _handler => (CreateGridEventHandler)Handler;

        /// <summary>
        /// Command name for MCP protocol
        /// </summary>
        public override string CommandName => "create_grid";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateGridCommand(UIApplication uiApp)
            : base(new CreateGridEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    GridCreationInfo data = parameters.ToObject<GridCreationInfo>();

                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Grid creation data is null");

                    // Validate before processing
                    if (!data.Validate(out string validationError))
                    {
                        throw new ArgumentException($"Invalid grid parameters: {validationError}");
                    }

                    // Set parameters and trigger event
                    _handler.SetParameters(data);

                    // Wait for completion with 10 second timeout
                    if (RaiseAndWaitForCompletion(10000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Grid creation operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create grid system: {ex.Message}");
                }
                    }
        }
    }
}
