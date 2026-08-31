using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;

namespace RevitMCPCommandSet.Commands.Views
{
    public class PlaceViewOnSheetCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private PlaceViewOnSheetEventHandler _handler => (PlaceViewOnSheetEventHandler)Handler;

        public override string CommandName => "place_view_on_sheet";

        public PlaceViewOnSheetCommand(UIApplication uiApp)
            : base(new PlaceViewOnSheetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    List<ViewportCreationInfo> data = new List<ViewportCreationInfo>();
                    data = parameters["data"].ToObject<List<ViewportCreationInfo>>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Input data from AI is null");

                    _handler.SetParameters(data);

                    if (RaiseAndWaitForCompletion(15000))
                        return _handler.Result;
                    else
                        throw new TimeoutException("Place viewport operation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to place view on sheet: {ex.Message}");
                }
                    }
        }
    }
}
