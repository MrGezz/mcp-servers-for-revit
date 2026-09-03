using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Query
{
    public class QueryParametersEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int ElementId { get; private set; }
        public AIResult<List<object>> Result { get; private set; }

        public void SetParameters(int elementId)
        {
            ElementId = elementId;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var element = Doc.GetElement(ElementIdFactory.Create(ElementId));
                if (element == null)
                {
                    Result = new AIResult<List<object>> { Success = false, Message = $"Element {ElementId} not found" };
                    return;
                }
                var parameters = new List<object>();
                foreach (Parameter param in element.Parameters)
                {
                    parameters.Add(new
                    {
                        Name = param.Definition?.Name ?? "Unknown",
                        Value = param.AsValueString() ?? param.AsString() ?? param.AsInteger().ToString(),
                        StorageType = param.StorageType.ToString(),
                        IsReadOnly = param.IsReadOnly
                    });
                }
                Result = new AIResult<List<object>> { Success = true, Response = parameters };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<object>> { Success = false, Message = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Query Parameters";
    }
}
