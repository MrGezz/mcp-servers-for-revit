using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Dynamo;

namespace RevitMCPCommandSet.Commands.Dynamo
{
    /// <summary>
    /// One command, with the operation in the parameters.
    /// </summary>
    /// <remarks>
    /// ONE COMMAND, NOT THREE. Every command in this set costs an entry in
    /// command.json, a registry slot the user has to enable in the Settings
    /// dialog, and a tool in the MCP client's list. status/open/run are three
    /// facets of one capability that is either present or absent together, so
    /// they ship as one command with an "op" — the same shape the MCP server's
    /// Dynamo backend expects, which is what lets the TypeScript side treat this
    /// and any external bridge as interchangeable.
    ///
    /// NO DRY RUN. Every other write in this set can be wrapped in a transaction
    /// and rolled back. A Dynamo graph opens and commits its own transactions in
    /// its own order, so there is nothing to roll back into. The MCP tool gates
    /// "run" behind an explicit confirm instead of offering a rollback that
    /// cannot exist.
    /// </remarks>
    public class DynamoCommand : ExternalEventCommandBase
    {
        private readonly object _executionLock = new object();
        private DynamoEventHandler _handler => (DynamoEventHandler)Handler;

        public override string CommandName => "dynamo_op";

        public DynamoCommand(UIApplication uiApp)
            : base(new DynamoEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string op = parameters?["op"]?.ToString() ?? "status";
                    string path = parameters?["path"]?.ToString();

                    _handler.Op = op;
                    _handler.GraphPath = path;

                    // A graph run is not a quick call: it can evaluate for
                    // minutes on the API thread. status and open are fast, so
                    // they do not inherit a run's patience.
                    int timeout = op == "run" ? 600000 : 60000;

                    if (RaiseAndWaitForCompletion(timeout))
                    {
                        return _handler.Result;
                    }

                    throw new TimeoutException(
                        $"Dynamo operation \"{op}\" did not complete within {timeout / 1000}s. " +
                        "A Dynamo graph runs on Revit's API thread, so a long evaluation blocks the UI until it finishes.");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Dynamo operation failed: {ex.Message}");
                }
            }
        }
    }
}
