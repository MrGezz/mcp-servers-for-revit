using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class LoadFamilyEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public string FilePath { get; private set; }
        public string FamilyName { get; private set; }

        public AIResult<bool> Result { get; private set; }

        public void SetParameters(string filePath, string familyName)
        {
            FilePath = filePath;
            FamilyName = familyName;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                if (string.IsNullOrEmpty(FilePath))
                {
                    Result = new AIResult<bool> { Success = false, Message = "File path is required" };
                    return;
                }

                // Document.LoadFamily MUST NOT run inside an open Transaction: the family
                // loader starts and manages its own, and nesting it is unsupported and a
                // documented crash route. It is called here with no transaction open.
                //
                // Document.LoadFamily(String, IFamilyLoadOptions, Family) is present and
                // identical across Revit 2022-2027; no version guard is required. The
                // two-argument (String, IFamilyLoadOptions) form exists in no version.
                FamilyLoadOptions loadOptions = new FamilyLoadOptions();
                bool loaded = doc.LoadFamily(FilePath, loadOptions, out Family _);

                using (Transaction trans = new Transaction(doc, "Load Family"))
                {
                    trans.Start();

                    if (loaded)
                    {
                        trans.Commit();

                        if (!string.IsNullOrEmpty(FamilyName))
                        {
                            Family family;
                            using (var familyCollector = new FilteredElementCollector(doc))
                            {
                                family = familyCollector
                                    .OfClass(typeof(Family))
                                    .Cast<Family>()
                                    .FirstOrDefault(f => f.Name == FamilyName);
                            }

                            if (family == null)
                            {
                                Result = new AIResult<bool>
                                {
                                    Success = true,
                                    Message = $"Family loaded from '{FilePath}' but specified family name '{FamilyName}' not found in project",
                                    Response = true
                                };
                                return;
                            }
                        }

                        Result = new AIResult<bool>
                        {
                            Success = true,
                            Message = $"Family loaded successfully from '{FilePath}'",
                            Response = true
                        };
                    }
                    else
                    {
                        trans.RollBack();
                        Result = new AIResult<bool>
                        {
                            Success = false,
                            Message = $"Failed to load family from '{FilePath}'",
                            Response = false
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool>
                {
                    Success = false,
                    Message = $"Error loading family: {ex.Message}",
                    Response = false
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Load Family";
    }

    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
