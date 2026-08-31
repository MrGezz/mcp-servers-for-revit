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
    public class OperateElementCommand : ExternalEventCommandBase
    {
        // Instance-level, not static: ExternalEvent.Raise() already serialises
        // EXECUTION on the Revit UI thread, so a static lock would serialise
        // unrelated commands against each other for no benefit. What is
        // unprotected is this command's SHARED HANDLER INSTANCE - the registry
        // keeps one per command name - between SetParameters() and the handler
        // reading those parameters on the UI thread.
        private readonly object _executionLock = new object();

        private OperateElementEventHandler _handler => (OperateElementEventHandler)Handler;

        /// <summary>
        /// Command name.
        /// </summary>
        public override string CommandName => "operate_element";

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public OperateElementCommand(UIApplication uiApp)
            : base(new OperateElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    OperationSetting data = new OperationSetting();
                    // Parse parameters
                    data = parameters["data"].ToObject<OperationSetting>();
                    if (data == null)
                        throw new ArgumentNullException(nameof(data), "Input data from AI is null.");

                    // Set point-element parameters
                    _handler.SetParameters(data);

                    // Raise the external event and wait for completion
                    if (RaiseAndWaitForCompletion(10000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Operate element timed out.");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Operate element failed: {ex.Message}");
                }
                    }
        }
    }
}
