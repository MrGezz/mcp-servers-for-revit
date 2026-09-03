using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Modify
{
    public class ManageProjectParametersEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public string Action { get; private set; }
        public string SharedParamFile { get; private set; }
        public string ParamGroup { get; private set; }
        public JArray Params { get; private set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, string sharedParamFile, string paramGroup, JArray paramList)
        {
            Action = action;
            SharedParamFile = sharedParamFile;
            ParamGroup = paramGroup;
            Params = paramList;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                switch (Action.ToLower())
                {
                    case "list":
                        Result = ListProjectParameters();
                        break;

                    case "add":
                        Result = AddSharedParameters();
                        break;

                    default:
                        throw new ArgumentException($"Unsupported action: {Action}. Supported: list, add");
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private AIResult<object> ListProjectParameters()
        {
            var bindingMap = Doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            var parameters = new List<object>();
            while (iterator.MoveNext())
            {
                var def = iterator.Key;
                var binding = iterator.Current as ElementBinding;
                var categories = binding?.Categories.Cast<Category>().Select(c => c.Name).ToList();
                parameters.Add(new
                {
                    Name = def.Name,
                    // 2023+ builds define REVIT2023_OR_GREATER AND REVIT2025_OR_GREATER, so
                    // a second #elif on the latter could never be reached and every modern
                    // build answered Group = "PG_DATA", Visible = true for every parameter.
#if REVIT2023_OR_GREATER
                    ParameterType = def.GetDataType().ToString(),
                    Group = GroupLabel(def),
#else
                    ParameterType = def.ParameterType.ToString(),
                    Group = def.ParameterGroup.ToString(),
#endif
                    Visible = (def as InternalDefinition)?.Visible ?? true,
                    Categories = categories
                });
            }
            return new AIResult<object> { Success = true, Response = parameters };
        }

#if REVIT2023_OR_GREATER
        /// <summary>
        /// Display label of the parameter's group ("Data", "Identity Data", ...), or ""
        /// when the definition carries no group. ForgeTypeId groups replaced
        /// BuiltInParameterGroup in the 2022+ API.
        /// </summary>
        private static string GroupLabel(Definition def)
        {
            try
            {
                var group = def.GetGroupTypeId();
                if (group == null || string.IsNullOrEmpty(group.TypeId)) return string.Empty;
                return LabelUtils.GetLabelForGroup(group);
            }
            catch
            {
                return string.Empty;
            }
        }
#endif

        private AIResult<object> AddSharedParameters()
        {
            if (string.IsNullOrEmpty(SharedParamFile))
                throw new ArgumentException("sharedParamFile is required for add action");
            if (Params == null || Params.Count == 0)
                throw new ArgumentException("params array is required for add action");

            var app = uiApp.Application;
            app.SharedParametersFilename = SharedParamFile;
            var sharedParamFile = app.OpenSharedParameterFile();
            if (sharedParamFile == null)
                throw new Exception($"Could not open shared parameter file at '{SharedParamFile}'.");

            var group = sharedParamFile.Groups.get_Item(ParamGroup ?? "General");
            if (group == null)
                throw new ArgumentException($"Shared parameter group '{ParamGroup}' not found in file");

            var bindingMap = Doc.ParameterBindings;
            using (var trans = new Transaction(Doc, "Add Project Parameters"))
            {
                trans.Start();
                foreach (var item in Params)
                {
                    var paramObj = item as JObject;
                    if (paramObj == null) continue;

                    string paramName = paramObj["name"]?.Value<string>();
                    var categoryNames = paramObj["categories"]?.ToObject<List<string>>();

                    if (string.IsNullOrEmpty(paramName)) continue;

                    var sharedParam = group.Definitions.get_Item(paramName);
                    if (sharedParam == null)
                        throw new ArgumentException($"Shared parameter '{paramName}' not found in group '{ParamGroup}'");

                    var newBinding = app.Create.NewInstanceBinding();
                    if (categoryNames != null && categoryNames.Count > 0)
                    {
                        var catSet = new CategorySet();
                        var unresolvedCategories = new List<string>();
                        foreach (var catName in categoryNames)
                        {
                            // Category.GetCategory has no string-name overload in any version;
                            // iterate Settings.Categories by name across all Revit versions.
                            Category cat = null;
                            var allCats = Doc.Settings.Categories;
                            foreach (Category c in allCats)
                            {
                                if (c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase))
                                {
                                    cat = c;
                                    break;
                                }
                            }
                            if (cat != null)
                                catSet.Insert(cat);
                            else
                                unresolvedCategories.Add(catName);
                        }
                        if (unresolvedCategories.Count > 0)
                            throw new ArgumentException($"The following category names could not be resolved: {string.Join(", ", unresolvedCategories)}");
                        newBinding.Categories = catSet;
                    }
                    else
                    {
                        var catSet = new CategorySet();
                        catSet.Insert(Category.GetCategory(Doc, BuiltInCategory.OST_GenericModel));
                        newBinding.Categories = catSet;
                    }

#if REVIT2025_OR_GREATER
                    // Revit 2025+: BuiltInParameterGroup enum removed; use GroupTypeId (ForgeTypeId)
                    bindingMap.Insert(sharedParam, newBinding, GroupTypeId.Data);
#else
                    // Revit 2022-2024: BuiltInParameterGroup still available
                    bindingMap.Insert(sharedParam, newBinding, BuiltInParameterGroup.PG_DATA);
#endif
                }
                trans.Commit();
            }
            return new AIResult<object> { Success = true, Response = true };
        }

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Manage Project Parameters";
    }
}
