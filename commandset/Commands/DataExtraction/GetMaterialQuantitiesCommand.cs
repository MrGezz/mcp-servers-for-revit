using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class GetMaterialQuantitiesCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private GetMaterialQuantitiesEventHandler _handler => (GetMaterialQuantitiesEventHandler)Handler;

        public override string CommandName => "get_material_quantities";

        public GetMaterialQuantitiesCommand(UIApplication uiApp)
            : base(new GetMaterialQuantitiesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    List<string> categoryFilters = parameters?["categoryFilters"]?.ToObject<List<string>>();
                    bool selectedElementsOnly = parameters?["selectedElementsOnly"]?.Value<bool>() ?? false;

                    // Set parameters
                    // The third argument was previously omitted, so element ids were stripped
                    // unconditionally while the success message told callers to pass a flag that
                    // never reached the handler. Wire it up.
                    bool includeElementIds = parameters?["includeElementIds"]?.Value<bool>() ?? false;
                    _handler.SetParameters(categoryFilters, selectedElementsOnly, includeElementIds);

                    // Execute and wait
                    if (RaiseAndWaitForCompletion(120000)) // 120 second timeout for large projects
                    {
                        return _handler.ResultInfo;
                    }
                    else
                    {
                        throw new TimeoutException("Material quantities calculation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to get material quantities: {ex.Message}");
                }
                    }
        }
    }
}
