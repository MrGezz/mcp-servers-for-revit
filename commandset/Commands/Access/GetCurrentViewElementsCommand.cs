using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCurrentViewElementsCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private GetCurrentViewElementsEventHandler _handler => (GetCurrentViewElementsEventHandler)Handler;

        public override string CommandName => "get_current_view_elements";

        public GetCurrentViewElementsCommand(UIApplication uiApp)
            : base(new GetCurrentViewElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    List<string> modelCategoryList = parameters?["modelCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                    List<string> annotationCategoryList = parameters?["annotationCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                    bool includeHidden = parameters?["includeHidden"]?.Value<bool>() ?? false;
                    int limit = parameters?["limit"]?.Value<int>() ?? 100;

                    // Set query parameters
                    _handler.SetQueryParameters(modelCategoryList, annotationCategoryList, includeHidden, limit);

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(60000)) // 60-second timeout
                    {
                        return _handler.ResultInfo;
                    }
                    else
                    {
                        throw new TimeoutException("Get view elements timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to get view elements: {ex.Message}");
                }
                    }
        }
    }
}
