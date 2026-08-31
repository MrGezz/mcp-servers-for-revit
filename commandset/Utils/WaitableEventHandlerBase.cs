using System;
using System.Threading;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Owns the completion signal every waitable external-event handler needs,
    /// and disposes it.
    ///
    /// WHY THIS EXISTS. Every handler in this command set declared its own
    /// <see cref="ManualResetEvent"/> and none of them disposed it. A
    /// ManualResetEvent holds a Win32 kernel event handle; one handler instance
    /// is created per command and lives for the whole Revit session, so the
    /// handles accumulated for the life of the process.
    ///
    /// This type deliberately does NOT implement IExternalEventHandler or
    /// IWaitableExternalEventHandler. Declaring them here would force Execute()
    /// and GetName() to be abstract, and every derived handler would then need
    /// an 'override' keyword added to both. Leaving the interfaces on the
    /// derived classes keeps the change to each handler down to its base name
    /// and one deleted field.
    /// </summary>
    public abstract class WaitableEventHandlerBase : IDisposable
    {
        /// <summary>
        /// Signalled by the handler when Execute() finishes; waited on by the
        /// calling thread. Protected rather than private because the derived
        /// handlers Set() and Reset() it directly, exactly as they did when they
        /// each declared their own.
        /// </summary>
        protected readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                // Release any thread parked in WaitForCompletion before the handle
                // goes away, so disposal cannot strand a caller for its timeout.
                try { _resetEvent.Set(); } catch (ObjectDisposedException) { }
                _resetEvent.Dispose();
            }
        }

        ~WaitableEventHandlerBase()
        {
            Dispose(false);
        }
    }
}
