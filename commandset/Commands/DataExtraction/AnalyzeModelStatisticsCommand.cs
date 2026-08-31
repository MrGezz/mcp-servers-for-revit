using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class AnalyzeModelStatisticsCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private AnalyzeModelStatisticsEventHandler _handler => (AnalyzeModelStatisticsEventHandler)Handler;

        public override string CommandName => "analyze_model_statistics";

        public AnalyzeModelStatisticsCommand(UIApplication uiApp)
            : base(new AnalyzeModelStatisticsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    // Default FALSE, matching the handler and the tool schema. This response was
                    // measured at 181,717 characters, 95.2% of it the per-type breakdown, and it
                    // overflowed the client limit on an ordinary model.
                    bool includeDetailedTypes = parameters?["includeDetailedTypes"]?.Value<bool>() ?? false;

                    // Set parameters
                    _handler.SetParameters(includeDetailedTypes);

                    // Execute and wait
                    if (RaiseAndWaitForCompletion(120000)) // 120 second timeout for large models
                    {
                        return _handler.ResultInfo;
                    }
                    else
                    {
                        throw new TimeoutException("Model statistics analysis timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to analyze model statistics: {ex.Message}");
                }
                    }
        }
    }
}
