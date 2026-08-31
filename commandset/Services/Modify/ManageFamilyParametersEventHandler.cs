using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Modify
{
    public class ManageFamilyParametersEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public string Action { get; private set; }
        public int FamilyId { get; private set; }
        public string Name { get; private set; }
        public string NewName { get; private set; }
        public string Formula { get; private set; }
        public string ParamType { get; private set; }
        public AIResult<bool> Result { get; private set; }

        public void SetParameters(string action, int familyId, string name, string newName, string formula, string paramType)
        {
            Action = action;
            FamilyId = familyId;
            Name = name;
            NewName = newName;
            Formula = formula;
            ParamType = paramType;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var family = Doc.GetElement(ElementIdFactory.Create(FamilyId)) as Family;
                if (family == null)
                {
                    Result = new AIResult<bool> { Success = false, Message = $"Family {FamilyId} not found" };
                    return;
                }
                var familyManager = Doc.FamilyManager;
                if (familyManager == null)
                {
                    Result = new AIResult<bool> { Success = false, Message = "Family document not open or FamilyManager unavailable" };
                    return;
                }
                using (var trans = new Transaction(Doc, $"Manage Family Parameters - {Action}"))
                {
                    trans.Start();
                    switch (Action.ToLower())
                    {
                        case "add":
                            if (string.IsNullOrEmpty(Name))
                                throw new ArgumentException("name is required for add action");
#if REVIT2022_OR_GREATER
                            // AddParameter(String, ForgeTypeId groupTypeId, ForgeTypeId specTypeId, Boolean isInstance)
                            // exists with this exact signature in Revit 2022-2027.
                            // ParamType is expected to be a valid ForgeTypeId type-id string (e.g. SpecTypeId.Number);
                            // falls back to SpecTypeId.Number when omitted. Group defaults to GroupTypeId.Data,
                            // isInstance defaults to true - the caller has no parameters to control these.
                            var specTypeId = string.IsNullOrEmpty(ParamType)
                                ? SpecTypeId.Number
                                : new ForgeTypeId(ParamType);
                            familyManager.AddParameter(Name, GroupTypeId.Data, specTypeId, true);
#else
                            // ForgeTypeId, SpecTypeId and GroupTypeId are all absent from the
                            // Revit 2020 and 2021 API, and the pre-ForgeTypeId AddParameter took
                            // a ParameterType/BuiltInParameterGroup pair that does not map back
                            // from the ForgeTypeId string this tool accepts. Refusing loudly is
                            // correct: silently adding a parameter of the wrong spec would be
                            // worse than not adding one.
                            throw new NotSupportedException(
                                "Adding a family parameter requires Revit 2022 or later. This build targets " +
                                "Revit 2020/2021, whose API predates ForgeTypeId, SpecTypeId and GroupTypeId. " +
                                "Add the parameter in the Family Types dialog instead.");
#endif
                            break;

                        case "rename":
                            if (string.IsNullOrEmpty(Name) || string.IsNullOrEmpty(NewName))
                                throw new ArgumentException("name and newName are required for rename action");
                            var fp = familyManager.get_Parameter(Name);
                            if (fp == null)
                                throw new ArgumentException($"Parameter '{Name}' not found in family");
                            familyManager.RenameParameter(fp, NewName);
                            break;

                        case "remove":
                            if (string.IsNullOrEmpty(Name))
                                throw new ArgumentException("name is required for remove action");
                            var fpRemove = familyManager.get_Parameter(Name);
                            if (fpRemove == null)
                                throw new ArgumentException($"Parameter '{Name}' not found in family");
                            familyManager.RemoveParameter(fpRemove);
                            break;

                        case "set_formula":
                            if (string.IsNullOrEmpty(Name))
                                throw new ArgumentException("name is required for set_formula action");
                            var fpFormula = familyManager.get_Parameter(Name);
                            if (fpFormula == null)
                                throw new ArgumentException($"Parameter '{Name}' not found in family");
                            familyManager.SetFormula(fpFormula, Formula ?? "");
                            break;

                        default:
                            throw new ArgumentException($"Unsupported action: {Action}");
                    }
                    trans.Commit();
                }
                Result = new AIResult<bool> { Success = true, Response = true };
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool> { Success = false, Message = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Manage Family Parameters";
    }
}
