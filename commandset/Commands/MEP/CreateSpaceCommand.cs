using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.MEP;
using RevitMCPCommandSet.Services.MEP;

namespace RevitMCPCommandSet.Commands.MEP
{
  public class CreateSpaceCommand : ExternalEventCommandBase
  {
      // Instance-level, not static: ExternalEvent.Raise() already serialises
      // EXECUTION on the Revit UI thread, so a static lock would serialise
      // unrelated commands against each other for no benefit. What is
      // unprotected is this command's SHARED HANDLER INSTANCE - the registry
      // keeps one per command name - between SetParameters() and the handler
      // reading those parameters on the UI thread.
      private readonly object _executionLock = new object();

    private CreateSpaceEventHandler _handler => (CreateSpaceEventHandler)Handler;
    public override string CommandName => "create_space";
    public CreateSpaceCommand(UIApplication uiApp)
        : base(new CreateSpaceEventHandler(), uiApp)
    {
    }
    public override object Execute(JObject parameters, string requestId)
    {
        lock (_executionLock)
        {
          try
          {
            List<SpaceCreationInfo> data = new List<SpaceCreationInfo>();
            data = parameters["data"].ToObject<List<SpaceCreationInfo>>();
            if (data == null)
              throw new ArgumentNullException(nameof(data), "AI input data is null");
            _handler.SetParameters(data);
            if (RaiseAndWaitForCompletion(15000))
              return _handler.Result;
            else
              throw new TimeoutException("Create space operation timed out");
          }
          catch (Exception ex)
          {
            throw new Exception($"Failed to create space: {ex.Message}");
          }
            }
    }
  }
}
