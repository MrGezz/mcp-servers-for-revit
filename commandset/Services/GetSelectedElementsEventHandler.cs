using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetSelectedElementsEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public List<Models.Common.ElementInfo> ResultElements { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        /// <summary>
        /// Why the read failed, or null when it did not. The command turns this into an
        /// error; without it a failure was indistinguishable from an empty answer.
        /// </summary>
        public string ErrorMessage { get; private set; }

        // Limit on the number of elements returned
        public int? Limit { get; set; }

        // IWaitableExternalEventHandler implementation
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;

                // Get the currently selected elements
                var selectedIds = uiDoc.Selection.GetElementIds();
                var selectedElements = selectedIds.Select(id => doc.GetElement(id)).ToList();

                // Apply the count limit
                if (Limit.HasValue && Limit.Value > 0)
                {
                    selectedElements = selectedElements.Take(Limit.Value).ToList();
                }

                // Convert to a list of ElementInfo
                ResultElements = selectedElements.Select(element => new ElementInfo
                {
                    Id = element.Id.GetValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    Category = element.Category?.Name,
                    Properties = GetElementProperties(element)
                }).ToList();
            }
            catch (Exception ex)
            {
                // The empty list assigned below made a FAILURE indistinguishable from
                // "nothing is selected". Record the reason so the command can raise it.
                ErrorMessage = "Failed to retrieve selected elements: " + ex.Message;
                Diagnostics.Report("Error", ErrorMessage);
                ResultElements = new List<Models.Common.ElementInfo>();
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        // Collect all parameters for the element, resolving names for Family, Type, and any
        // ElementId-typed parameter so the caller sees display strings rather than raw ids.
        private static Dictionary<string, string> GetElementProperties(Element element)
        {
            var properties = new Dictionary<string, string>();
            var doc = element.Document;

            // Resolve Family and Type by name first so they are never raw ids.
            if (element is FamilyInstance fi)
            {
                properties["Family"] = fi.Symbol?.FamilyName ?? "";
                properties["Type"]   = fi.Symbol?.Name ?? element.Name;
            }
            else
            {
                var typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeElem = doc.GetElement(typeId);
                    if (typeElem != null)
                    {
                        properties["Type"] = typeElem.Name;
                        Parameter familyParam = typeElem.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                        if (familyParam != null)
                            properties["Family"] = familyParam.AsString() ?? "";
                    }
                }
            }

            // Collect all element parameters as display strings.
            foreach (Parameter param in element.Parameters)
            {
                if (param?.Definition == null)
                    continue;

                string paramName = param.Definition.Name;
                if (string.IsNullOrWhiteSpace(paramName))
                    continue;

                // Family and Type are already resolved above; skip any duplicate.
                if (paramName == "Family" || paramName == "Type")
                    continue;

                // Skip duplicate keys (a parameter name can appear multiple times on an element
                // when it is shared across disciplines).
                if (properties.ContainsKey(paramName))
                    continue;

                string value;
                switch (param.StorageType)
                {
                    case StorageType.String:
                        value = param.AsString() ?? "";
                        break;

                    case StorageType.Double:
                        // AsValueString includes the unit suffix as shown in the UI.
                        value = param.AsValueString() ?? param.AsDouble().ToString("F4");
                        break;

                    case StorageType.Integer:
                        value = param.AsValueString() ?? param.AsInteger().ToString();
                        break;

                    case StorageType.ElementId:
                        var eid = param.AsElementId();
                        if (eid != null && eid != ElementId.InvalidElementId)
                        {
                            var refElem = doc.GetElement(eid);
                            value = refElem?.Name ?? eid.GetValue().ToString();
                        }
                        else
                        {
                            value = "";
                        }
                        break;

                    default:
                        continue;
                }

                properties[paramName] = value;
            }

            return properties;
        }

        public string GetName()
        {
            return "Get Selected Elements";
        }
    }
}
