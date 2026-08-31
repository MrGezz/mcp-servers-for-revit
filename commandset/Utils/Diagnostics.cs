using System.Diagnostics;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Non-blocking diagnostic output for command handlers.
    ///
    /// This exists because TaskDialog.Show MUST NOT be called from a command
    /// handler. Handlers run on Revit's API thread via ExternalEvent, and a modal
    /// dialog raised there blocks the ExternalEvent queue that EVERY other command
    /// shares. One unrecognised element id was enough to take the whole bridge down:
    /// the client saw "timeout" - which points diagnosis at the network layer - while
    /// the real cause was a dialog sitting on a screen nobody was looking at.
    ///
    /// It was also unrecoverable without two separate physical interactions:
    /// dismissing the dialog did not release the queue, because Revit was left in an
    /// active-command state until Escape cleared it.
    ///
    /// So: report through the RESULT the caller receives, and use this for anything
    /// that is genuinely developer diagnostics rather than a user-facing answer.
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Record a diagnostic. Never blocks, never opens a window, never throws.
        /// Visible in DebugView / the Visual Studio output window while debugging.
        /// </summary>
        public static void Report(string title, string message)
        {
            try
            {
                Trace.WriteLine("[revit-mcp] " + (title ?? "info") + ": " + (message ?? string.Empty));
            }
            catch
            {
                // A diagnostic sink that can take down the operation it is reporting on
                // is worse than no sink at all.
            }
        }
    }
}
