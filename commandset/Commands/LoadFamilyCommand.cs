using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands
{
    public class LoadFamilyCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private LoadFamilyEventHandler _handler => (LoadFamilyEventHandler)Handler;

        public override string CommandName => "load_family";

        public LoadFamilyCommand(UIApplication uiApp)
            : base(new LoadFamilyEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    string filePath = parameters["filePath"]?.Value<string>();
                    string familyName = parameters["familyName"]?.Value<string>();

                    _handler.SetParameters(filePath, familyName);

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Load family operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load family: {ex.Message}");
                }
                    }
        }
    }
}
