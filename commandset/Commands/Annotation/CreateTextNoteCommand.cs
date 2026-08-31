using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Services.Annotation;

namespace RevitMCPCommandSet.Commands.Annotation
{
    public class CreateTextNoteCommand : ExternalEventCommandBase
    {
        private CreateTextNoteEventHandler _handler => (CreateTextNoteEventHandler)Handler;

        public override string CommandName => "create_text_note";

        public CreateTextNoteCommand(UIApplication uiApp)
            : base(new CreateTextNoteEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                List<TextNoteCreationInfo> data = new List<TextNoteCreationInfo>();
                data = parameters["data"].ToObject<List<TextNoteCreationInfo>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "Input data from AI is null");

                _handler.SetParameters(data);

                if (RaiseAndWaitForCompletion(15000))
                    return _handler.Result;
                else
                    throw new TimeoutException("Text note creation timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create text note: {ex.Message}");
            }
        }
    }
}
