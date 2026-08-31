using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateDraftingViewEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public string ViewName { get; private set; }
        public int Scale { get; private set; }
        public string DetailLevel { get; private set; }

        public AIResult<int> Result { get; private set; }

        public void SetParameters(string name, int scale, string detailLevel)
        {
            ViewName = name;
            Scale = scale;
            DetailLevel = detailLevel;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Create Drafting View"))
                {
                    trans.Start();

                    ViewFamilyType vft;
                    using (var collector = new FilteredElementCollector(doc))
                    {
                        vft = collector
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vftype => vftype.ViewFamily == ViewFamily.Drafting);
                    }

                    if (vft == null)
                    {
                        Result = new AIResult<int> { Success = false, Message = "No drafting view family type found" };
                        return;
                    }

                    ViewDrafting view = ViewDrafting.Create(doc, vft.Id);

                    if (!string.IsNullOrEmpty(ViewName))
                    {
                        view.Name = ViewName;
                    }

                    if (Scale > 0)
                    {
                        // Defect 26. This replaced
                        // get_Parameter(BuiltInParameter.VIEW_SCALE)?.Set(Scale),
                        // where the '?.' guarded only a NULL parameter and did nothing
                        // about a non-null READ-ONLY one.
                        //
                        // The catch is not optional and it is not symmetry for its own
                        // sake: View.Scale throws when a view template controls the
                        // scale, and an uncaught throw here escapes the external-event
                        // handler and loses the whole drafting view over an optional
                        // field. The sibling fixes in SetViewProperties and CreateView
                        // guard the same way.
                        try
                        {
                            view.Scale = Scale;
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        {
                            // View created; scale is template-controlled and was not applied.
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }

                    if (!string.IsNullOrEmpty(DetailLevel))
                    {
                        switch (DetailLevel.ToLowerInvariant())
                        {
                            case "coarse":
                                view.DetailLevel = ViewDetailLevel.Coarse;
                                break;
                            case "medium":
                                view.DetailLevel = ViewDetailLevel.Medium;
                                break;
                            case "fine":
                                view.DetailLevel = ViewDetailLevel.Fine;
                                break;
                        }
                    }

                    int viewId = view.Id.GetIntValue();

                    trans.Commit();

                    Result = new AIResult<int>
                    {
                        Success = true,
                        Message = $"Drafting view '{view.Name}' created successfully",
                        Response = viewId
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<int>
                {
                    Success = false,
                    Message = $"Error creating drafting view: {ex.Message}"
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

        public string GetName() => "Create Drafting View";
    }
}
