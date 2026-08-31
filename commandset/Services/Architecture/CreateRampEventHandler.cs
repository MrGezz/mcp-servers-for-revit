using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Architecture
{
    public class CreateRampEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;


        public List<RampCreationInfo> RampData { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        public void SetParameters(List<RampCreationInfo> data)
        {
            RampData = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                // The Revit API (2022-2027) provides no public classes for programmatic
                // ramp creation. The types Ramp, RampType, RampRun, and
                // RampRunJustification do not exist in Autodesk.Revit.DB or
                // Autodesk.Revit.DB.Architecture on any supported Revit version.
                // Create ramps via the Revit UI (Architecture > Circulation > Ramp) instead.
                throw new NotSupportedException(
                    "Ramp creation is not supported: Revit 2022-2027 exposes no public " +
                    "ramp-creation API. The types Ramp, RampType, RampRun, and " +
                    "RampRunJustification do not exist in Autodesk.Revit.DB or " +
                    "Autodesk.Revit.DB.Architecture. Create ramps manually via the " +
                    "Revit UI (Architecture tab > Circulation panel > Ramp).");
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating ramps: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Create Ramp";
        }
    }
}