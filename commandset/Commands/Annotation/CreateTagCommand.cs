using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Services.Annotation;

namespace RevitMCPCommandSet.Commands.Annotation
{
    public class CreateTagCommand : ExternalEventCommandBase
    {
        private CreateTagEventHandler _handler => (CreateTagEventHandler)Handler;

        public override string CommandName => "create_tag";

        public CreateTagCommand(UIApplication uiApp)
            : base(new CreateTagEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                List<TagCreationInfo> data = new List<TagCreationInfo>();
                data = parameters["data"].ToObject<List<TagCreationInfo>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "Input data from AI is null");

                _handler.SetParameters(data);

                if (RaiseAndWaitForCompletion(15000))
                    return _handler.Result;
                else
                    throw new TimeoutException("Create tag operation timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create tag: {ex.Message}");
            }
        }
    }
}
