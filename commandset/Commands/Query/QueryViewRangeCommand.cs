using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Query;

namespace RevitMCPCommandSet.Commands.Query
{
    public class QueryViewRangeCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private QueryViewRangeEventHandler _handler => (QueryViewRangeEventHandler)Handler;
        public override string CommandName => "query_view_range";
        public QueryViewRangeCommand(UIApplication uiApp)
            : base(new QueryViewRangeEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"].Value<int>();
                    _handler.SetParameters(viewId);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Query view range timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to query view range: {ex.Message}");
                }
                    }
        }
    }
}
