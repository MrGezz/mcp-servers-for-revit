using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateElevationViewCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateElevationViewEventHandler _handler => (CreateElevationViewEventHandler)Handler;

        public override string CommandName => "create_elevation_view";

        public CreateElevationViewCommand(UIApplication uiApp)
            : base(new CreateElevationViewEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string name = parameters["name"]?.Value<string>();
                    int directionIndex = parameters["directionIndex"]?.Value<int>() ?? 0;
                    string viewFamilyTypeName = parameters["viewFamilyTypeName"]?.Value<string>() ?? "Elevation";

                    _handler.SetParameters(name, directionIndex, viewFamilyTypeName);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create elevation view operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create elevation view: {ex.Message}");
                }
                    }
        }
    }
}
