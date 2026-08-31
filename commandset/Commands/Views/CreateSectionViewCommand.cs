using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateSectionViewCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateSectionViewEventHandler _handler => (CreateSectionViewEventHandler)Handler;

        public override string CommandName => "create_section_view";

        public CreateSectionViewCommand(UIApplication uiApp)
            : base(new CreateSectionViewEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string name = parameters["name"]?.Value<string>();
                    JObject bbox = parameters["boundingBox"] as JObject;
                    string viewFamilyTypeName = parameters["viewFamilyTypeName"]?.Value<string>() ?? "Section";

                    double minX = bbox?["minX"]?.Value<double>() ?? -50;
                    double minY = bbox?["minY"]?.Value<double>() ?? -50;
                    double minZ = bbox?["minZ"]?.Value<double>() ?? -50;
                    double maxX = bbox?["maxX"]?.Value<double>() ?? 50;
                    double maxY = bbox?["maxY"]?.Value<double>() ?? 50;
                    double maxZ = bbox?["maxZ"]?.Value<double>() ?? 50;

                    _handler.SetParameters(name, minX, minY, minZ, maxX, maxY, maxZ, viewFamilyTypeName);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Create section view operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create section view: {ex.Message}");
                }
                    }
        }
    }
}
