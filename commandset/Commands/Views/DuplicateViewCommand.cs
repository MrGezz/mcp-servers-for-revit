using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class DuplicateViewCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private DuplicateViewEventHandler _handler => (DuplicateViewEventHandler)Handler;

        public override string CommandName => "duplicate_view";

        public DuplicateViewCommand(UIApplication uiApp)
            : base(new DuplicateViewEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int viewId = parameters["viewId"]?.Value<int>() ?? 0;
                    string mode = parameters["mode"]?.Value<string>() ?? "duplicate";
                    string newName = parameters["newName"]?.Value<string>();

                    _handler.SetParameters(viewId, mode, newName);

                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Duplicate view operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to duplicate view: {ex.Message}");
                }
                    }
        }
    }
}
