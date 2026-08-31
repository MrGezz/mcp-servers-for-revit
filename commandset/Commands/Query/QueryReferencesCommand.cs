using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Query;

namespace RevitMCPCommandSet.Commands.Query
{
    public class QueryReferencesCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private QueryReferencesEventHandler _handler => (QueryReferencesEventHandler)Handler;
        public override string CommandName => "query_references";
        public QueryReferencesCommand(UIApplication uiApp)
            : base(new QueryReferencesEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int elementId = parameters["elementId"].Value<int>();
                    _handler.SetParameters(elementId);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Query references timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to query references: {ex.Message}");
                }
                    }
        }
    }
}
