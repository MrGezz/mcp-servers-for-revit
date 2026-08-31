using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services.Architecture;

namespace RevitMCPCommandSet.Commands.Architecture
{
    public class CreateWallCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private CreateWallEventHandler _handler => (CreateWallEventHandler)Handler;

        public override string CommandName => "create_wall";

        public CreateWallCommand(UIApplication uiApp)
            : base(new CreateWallEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<WallCreationInfo> data = parameters["data"].ToObject<List<WallCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "No wall data provided");

                    _handler.SetParameters(data);

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create wall operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to create wall: {ex.Message}");
                }
                    }
        }
    }
}
