using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Modify;

namespace RevitMCPCommandSet.Commands.Modify
{
    public class DuplicateTypeCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private DuplicateTypeEventHandler _handler => (DuplicateTypeEventHandler)Handler;
        public override string CommandName => "duplicate_type";
        public DuplicateTypeCommand(UIApplication uiApp)
            : base(new DuplicateTypeEventHandler(), uiApp)
        {
        }
        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int typeId = parameters["typeId"].Value<int>();
                    string newName = parameters["newName"].Value<string>();
                    _handler.SetParameters(typeId, newName);
                    if (RaiseAndWaitForCompletion(10000))
                        return _handler.Result;
                    throw new TimeoutException("Duplicate type timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to duplicate type: {ex.Message}");
                }
                    }
        }
    }
}
