using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Delete
{
    public class DeleteElementCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteElementEventHandler _handler => (DeleteElementEventHandler)Handler;

        public override string CommandName => "delete_element";

        public DeleteElementCommand(UIApplication uiApp)
            : base(new DeleteElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse array parameters
                    var elementIds = parameters?["elementIds"]?.ToObject<string[]>();
                    if (elementIds == null || elementIds.Length == 0)
                    {
                        throw new ArgumentException("Element ID list must not be empty");
                    }

                    // Set the array of element IDs to delete
                    _handler.ElementIds = elementIds;

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        // Partial success is reported as such: the ids that could not be
                        // resolved travel back with the result rather than being shown in a
                        // dialog inside Revit, which the caller cannot see and which blocks
                        // every other command until somebody dismisses it.
                        if (_handler.IsSuccess)
                        {
                            return new
                            {
                                deleted = true,
                                count = _handler.DeletedCount,
                                unparseableIds = _handler.UnparseableIds,
                                missingIds = _handler.MissingIds
                            };
                        }

                        throw new Exception(_handler.ErrorMessage ?? "Failed to delete elements");
                    }
                    else
                    {
                        throw new TimeoutException("Delete element operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to delete elements: {ex.Message}");
                }
            }
        }
    }
}
