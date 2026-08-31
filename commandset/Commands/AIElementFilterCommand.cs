using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Commands
{
    public class AIElementFilterCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private AIElementFilterEventHandler _handler => (AIElementFilterEventHandler)Handler;

        /// <summary>
        /// Command name.
        /// </summary>
        public override string CommandName => "ai_element_filter";

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public AIElementFilterCommand(UIApplication uiApp)
            : base(new AIElementFilterEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    FilterSetting data = new FilterSetting();
                    // Parse parameters
                    data = parameters["data"].ToObject<FilterSetting>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Input data from AI is null");

                    // Set AI filter parameters
                    _handler.SetParameters(data);

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(10000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Element filter operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to retrieve element information: {ex.Message}");
                }
                    }
        }
    }
}
