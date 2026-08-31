using RevitMCPCommandSet.Utils;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public bool IsSuccess { get; private set; }

        // Number of elements successfully deleted
        public int DeletedCount { get; private set; }

        // Why a request did not fully succeed. Returned to the caller instead of
        // being shown in a modal dialog: a TaskDialog raised here blocks the
        // ExternalEvent queue that every other command shares, so one bad id used
        // to take the whole bridge down and report it to the client as a timeout.
        public List<string> UnparseableIds { get; private set; } = new List<string>();
        public List<string> MissingIds { get; private set; } = new List<string>();
        public string ErrorMessage { get; private set; }
        // State synchronization object
        public bool TaskCompleted { get; private set; }
        // Array of element IDs to delete
        public string[] ElementIds { get; set; }
        // IWaitableExternalEventHandler interface implementation
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }
        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                DeletedCount = 0;
                if (ElementIds == null || ElementIds.Length == 0)
                {
                    IsSuccess = false;
                    return;
                }
                // Build the list of element IDs to delete
                List<ElementId> elementIdsToDelete = new List<ElementId>();
                List<string> invalidIds = new List<string>();
                UnparseableIds.Clear();
                MissingIds.Clear();
                ErrorMessage = null;

                foreach (var idStr in ElementIds)
                {
                    if (int.TryParse(idStr, out int elementIdValue))
                    {
                        var elementId = ElementIdFactory.Create(elementIdValue);
                        if (doc.GetElement(elementId) != null)
                        {
                            elementIdsToDelete.Add(elementId);
                        }
                        else
                        {
                            // Previously dropped in silence: only UNPARSEABLE strings were
                            // recorded, so a well-formed id for an element that is not there
                            // vanished without trace while the message claimed to cover it.
                            MissingIds.Add(idStr);
                        }
                    }
                    else
                    {
                        UnparseableIds.Add(idStr);
                    }
                }

                invalidIds.AddRange(UnparseableIds);
                invalidIds.AddRange(MissingIds);
                // If there are elements to delete, proceed
                if (elementIdsToDelete.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Delete Elements"))
                    {
                        transaction.Start();

                        // Delete all elements in a single batch
                        ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                        DeletedCount = deletedIds.Count;

                        transaction.Commit();
                    }
                    IsSuccess = true;
                }
                else
                {
                    ErrorMessage = BuildIdReport("No elements were deleted");
                    IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to delete elements: " + ex.Message;
                IsSuccess = false;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }
        // One sentence naming exactly which ids failed and why, so the caller can
        // correct the request instead of guessing at a timeout.
        private string BuildIdReport(string prefix)
        {
            var parts = new List<string>();
            if (UnparseableIds.Count > 0)
                parts.Add("not valid element ids: " + string.Join(", ", UnparseableIds));
            if (MissingIds.Count > 0)
                parts.Add("no such element in this document: " + string.Join(", ", MissingIds));
            return parts.Count == 0 ? prefix : prefix + " - " + string.Join("; ", parts) + ".";
        }

        public string GetName()
        {
            return "Delete Elements";
        }
    }
}
