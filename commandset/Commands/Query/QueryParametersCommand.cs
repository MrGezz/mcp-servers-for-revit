using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Query;

namespace RevitMCPCommandSet.Commands.Query
{
    public class QueryParametersCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private QueryParametersEventHandler _handler => (QueryParametersEventHandler)Handler;
        public override string CommandName => "query_parameters";
        public QueryParametersCommand(UIApplication uiApp)
            : base(new QueryParametersEventHandler(), uiApp)
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
                    throw new TimeoutException("Query parameters timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to query parameters: {ex.Message}");
                }
                    }
        }
    }
}
