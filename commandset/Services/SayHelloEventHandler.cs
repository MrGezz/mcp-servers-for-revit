using RevitMCPCommandSet.Utils;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SayHelloEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {

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
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                RevitVersion = app.Application.VersionNumber;
                DocumentTitle = app.ActiveUIDocument?.Document?.Title ?? "(no document open)";
                DialogShown = false;

                // P1-5. The dialog is compiled out of release builds entirely, because
                // a modal TaskDialog raised here runs on the API thread and blocks the
                // ExternalEvent queue that EVERY other command shares.
                //
                // DialogShown is initialised false above and is deliberately left false
                // in release: that is the truth, not a lost signal. A release build
                // shows no dialog, so a response saying dialogShown=false describes what
                // actually happened. The showDialog request is honoured only where the
                // dialog can be honoured.
#if DEBUG
                if (ShowDialog)
                {
                    DialogShown = true;
                    TaskDialog.Show("Revit MCP", Message);
                }
#endif
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
