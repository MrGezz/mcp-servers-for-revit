using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands
{
    public class CreateSurfaceElementCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateSurfaceElementEventHandler _handler => (CreateSurfaceElementEventHandler)Handler;

        /// <summary>
        /// Command name
        /// </summary>
        public override string CommandName => "create_surface_based_element";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateSurfaceElementCommand(UIApplication uiApp)
            : base(new CreateSurfaceElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<SurfaceElement> data = new List<SurfaceElement>();
                    // Parse parameters
                    data = parameters["data"].ToObject<List<SurfaceElement>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Input data is null");

                    // Set surface element parameters
                    _handler.SetParameters(data);

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(10000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create surface element operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create surface element: {ex.Message}");
                }
                    }
        }
    }
}
