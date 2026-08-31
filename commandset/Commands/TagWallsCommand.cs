using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class TagWallsCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private TagWallsEventHandler _handler => (TagWallsEventHandler)Handler;

        /// <summary>
        /// Command name.
        /// </summary>
        public override string CommandName => "tag_walls";

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public TagWallsCommand(UIApplication uiApp)
            : base(new TagWallsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse parameters
                    bool useLeader = false;
                    if (parameters["useLeader"] != null)
                    {
                        useLeader = parameters["useLeader"].ToObject<bool>();
                    }

                    string tagTypeId = null;
                    if (parameters["tagTypeId"] != null)
                    {
                        tagTypeId = parameters["tagTypeId"].ToString();
                    }

                    // Set tagging parameters
                    _handler.SetParameters(useLeader, tagTypeId);

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(10000))
                    {
                        return _handler.TaggingResults;
                    }
                    else
                    {
                        throw new TimeoutException("Tag walls operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Tag walls failed: {ex.Message}");
                }
                    }
        }
    }
}