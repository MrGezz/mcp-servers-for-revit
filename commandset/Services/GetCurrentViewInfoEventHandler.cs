using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCurrentViewInfoEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public CurrentViewInfo ResultInfo { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        /// <summary>
        /// Why the read failed, or null when it did not. The command turns this into an
        /// error; without it a failure was indistinguishable from an empty answer.
        /// </summary>
        public string ErrorMessage { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Implements the IWaitableExternalEventHandler interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;
                var activeView = doc.ActiveView;

                ResultInfo = new CurrentViewInfo
                {
                    Id = activeView.Id.GetIntValue(),
                    UniqueId = activeView.UniqueId,
                    Name = activeView.Name,
                    ViewType = activeView.ViewType.ToString(),
                    IsTemplate = activeView.IsTemplate,
                    Scale = activeView.Scale,
                    DetailLevel = activeView.DetailLevel.ToString(),
                };
            }
            catch (Exception ex)
            {
                // The exception message was previously discarded and ResultInfo left null,
                // so the caller received null and read it as "this view has no info".
                ErrorMessage = "Failed to retrieve view info: " + ex.Message;
                Diagnostics.Report("error", ErrorMessage);
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Get current view info";
        }
    }
}
