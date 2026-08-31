using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Annotation
{
    public class CreateRevisionEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public string RevisionName { get; private set; }
        public string RevisionDate { get; private set; }
        public string RevisionNumber { get; private set; }
        public string RevisionDescription { get; private set; }

        public AIResult<int> Result { get; private set; }

        public void SetParameters(string name, string date, string number, string description)
        {
            RevisionName = name;
            RevisionDate = date;
            RevisionNumber = number;
            RevisionDescription = description;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Create Revision"))
                {
                    trans.Start();

                    Revision revision = Revision.Create(doc);

                    if (!string.IsNullOrEmpty(RevisionName))
                    {
                        revision.Description = RevisionName;
                    }

                    if (!string.IsNullOrEmpty(RevisionDate))
                    {
                        revision.RevisionDate = RevisionDate;
                    }

                    // Defect 35. SetRevisionNumber throws InvalidOperationException
                    // ("The parameter is read-only.") whenever the project's revision
                    // sequence uses AUTOMATIC numbering, which is the default. That
                    // exception used to escape to the outer catch, roll this
                    // transaction back, and leave NO REVISION CREATED AT ALL - the
                    // caller lost the whole operation over an optional field.
                    //
                    // Revision.NumberType does NOT exist on this API span: Revit 2026
                    // exposes RevisionNumber and RevisionNumberingSequenceId, and
                    // numbering is owned by the sequence, not the revision. So the
                    // number cannot be forced from here without reconfiguring a
                    // project-wide sequence, which is not this command's business.
                    // The revision is created either way and the caller is told
                    // plainly when the number could not be applied.
                    string numberNote = null;
                    if (!string.IsNullOrEmpty(RevisionNumber))
                    {
                        try
                        {
                            revision.SetRevisionNumber(RevisionNumber);
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        {
                            numberNote = $" (the number '{RevisionNumber}' was not applied: this project's revision sequence numbers revisions automatically)";
                        }
                        catch (InvalidOperationException)
                        {
                            numberNote = $" (the number '{RevisionNumber}' was not applied: this project's revision sequence numbers revisions automatically)";
                        }
                    }

                    if (!string.IsNullOrEmpty(RevisionDescription) && string.IsNullOrEmpty(RevisionName))
                    {
                        revision.Description = RevisionDescription;
                    }

                    int revisionId = revision.Id.GetIntValue();

                    trans.Commit();

                    Result = new AIResult<int>
                    {
                        Success = true,
                        Message = $"Revision '{revision.Description}' created successfully{numberNote}",
                        Response = revisionId
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<int>
                {
                    Success = false,
                    Message = $"Error creating revision: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Create Revision";
    }
}
