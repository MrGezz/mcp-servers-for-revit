using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Query;

namespace RevitMCPCommandSet.Commands.Query
{
    public class CheckInterferencesCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CheckInterferencesEventHandler _handler => (CheckInterferencesEventHandler)Handler;
        public override string CommandName => "check_interferences";
        public CheckInterferencesCommand(UIApplication uiApp)
            : base(new CheckInterferencesEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var elementIds = parameters["elementIds"].ToObject<int[]>();
                    _handler.SetParameters(elementIds);
                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;
                    throw new TimeoutException("Check interferences timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to check interferences: {ex.Message}");
                }
                    }
        }
    }
}
