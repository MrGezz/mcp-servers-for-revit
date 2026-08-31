using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Modify
{
    public class DuplicateTypeEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int TypeId { get; private set; }
        public string NewName { get; private set; }
        public AIResult<int> Result { get; private set; }

        public void SetParameters(int typeId, string newName)
        {
            TypeId = typeId;
            NewName = newName;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var element = Doc.GetElement(ElementIdFactory.Create(TypeId));
                if (element == null)
                {
                    Result = new AIResult<int> { Success = false, Message = $"Type element {TypeId} not found" };
                    return;
                }
                var elementType = element as ElementType;
                if (elementType == null)
                {
                    Result = new AIResult<int> { Success = false, Message = $"Element {TypeId} is not an ElementType" };
                    return;
                }
                using (var trans = new Transaction(Doc, "Duplicate Type"))
                {
                    trans.Start();
                    var newType = elementType.Duplicate(NewName);
                    trans.Commit();
                    int newTypeId = newType.Id.GetIntValue();
                    Result = new AIResult<int> { Success = true, Response = newTypeId };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<int> { Success = false, Message = ex.Message };
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

        public string GetName() => "Duplicate Type";
    }
}
