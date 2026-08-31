using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SayHelloEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Message { get; set; } = "Hello MCP!";

        /// <summary>
        /// Whether to open a modal dialog in Revit. FALSE by default.
        ///
        /// A TaskDialog raised from a command handler blocks the ExternalEvent queue
        /// that every other command shares, and this handler only sets its
        /// ManualResetEvent in finally - so the bridge stays blocked until a human
        /// dismisses the dialog. Measured at 15 s. Connection-testing should not
        /// require somebody to be sitting in front of Revit.
        /// </summary>
        public bool ShowDialog { get; set; }

        /// <summary>What the handler observed, returned instead of shown.</summary>
        public string DocumentTitle { get; private set; }
        public string RevitVersion { get; private set; }
        public bool DialogShown { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                RevitVersion = app.Application.VersionNumber;
                DocumentTitle = app.ActiveUIDocument?.Document?.Title ?? "(no document open)";
                DialogShown = false;

                if (ShowDialog)
                {
                    DialogShown = true;
                    TaskDialog.Show("Revit MCP", Message);
                }
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Say Hello";
        }
    }
}
