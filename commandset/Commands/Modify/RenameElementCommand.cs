using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class RenameElementCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private RenameElementEventHandler _handler => (RenameElementEventHandler)Handler;
        public override string CommandName => "rename_element";
        public RenameElementCommand(UIApplication uiApp)
            : base(new RenameElementEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int elementId = parameters["elementId"].Value<int>();
                    string newName = parameters["newName"].Value<string>();
                    _handler.SetParameters(elementId, newName);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Rename element timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to rename element: {ex.Message}");
                }
                    }
        }
    }
}
