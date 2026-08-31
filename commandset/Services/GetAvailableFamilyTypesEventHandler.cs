using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetAvailableFamilyTypesEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public List<FamilyTypeInfo> ResultFamilyTypes { get; private set; }

        // Status synchronization object
        public bool TaskCompleted { get; private set; }
        /// <summary>
        /// Why the read failed, or null when it did not. The command turns this into an
        /// error; without it a failure was indistinguishable from an empty answer.
        /// </summary>
        public string ErrorMessage { get; private set; }

        // Filter criteria
        public List<string> CategoryList { get; set; }
        public string FamilyNameFilter { get; set; }
        public int? Limit { get; set; }

        // Execution timeout, slightly shorter than the caller's timeout
        public bool WaitForCompletion(int timeoutMilliseconds = 12500)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Loadable families.
                //
                // P1-1. This chain ends in .Cast<FamilySymbol>(), which is LAZY, and
                // the result is enumerated further down when the system types are
                // merged. Hoisting the collector into a using block without forcing
                // enumeration first would dispose it before that merge reads it -
                // turning a handle leak into a use-after-dispose. So the results are
                // MATERIALISED inside the block, and the collector is disposed on the
                // way out with the elements already in hand.
                List<FamilySymbol> familySymbols;
                using (var symbolCollector = new FilteredElementCollector(doc))
                {
                    familySymbols = symbolCollector
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .ToList();
                }
                // System family types (walls, floors, etc.)
                var systemTypes = new List<ElementType>();
                using (var wc = new FilteredElementCollector(doc)) { systemTypes.AddRange(wc.OfClass(typeof(WallType)).Cast<ElementType>()); }
                using (var fc = new FilteredElementCollector(doc)) { systemTypes.AddRange(fc.OfClass(typeof(FloorType)).Cast<ElementType>()); }
                using (var rc = new FilteredElementCollector(doc)) { systemTypes.AddRange(rc.OfClass(typeof(RoofType)).Cast<ElementType>()); }
                using (var cc = new FilteredElementCollector(doc)) { systemTypes.AddRange(cc.OfClass(typeof(CeilingType)).Cast<ElementType>()); }
                using (var csc = new FilteredElementCollector(doc)) { systemTypes.AddRange(csc.OfClass(typeof(CurtainSystemType)).Cast<ElementType>()); }
                // Merge results
                var allElements = familySymbols
                    .Cast<ElementType>()
                    .Concat(systemTypes)
                    .ToList();

                IEnumerable<ElementType> filteredElements = allElements;

                // Category filter
                if (CategoryList != null && CategoryList.Any())
                {
                    var validCategoryIds = new List<int>();
                    foreach (var categoryName in CategoryList)
                    {
                        if (Enum.TryParse(categoryName, out BuiltInCategory bic))
                        {
                            validCategoryIds.Add((int)bic);
                        }
                    }

                    if (validCategoryIds.Any())
                    {
                        filteredElements = filteredElements.Where(et =>
                        {
                            var categoryId = et.Category?.Id.GetValue();
                            return categoryId != null && validCategoryIds.Contains((int)categoryId.Value);
                        });
                    }
                }

                // Fuzzy name match (checks both family name and type name)
                if (!string.IsNullOrEmpty(FamilyNameFilter))
                {
                    filteredElements = filteredElements.Where(et =>
                    {
                        string familyName = et is FamilySymbol fs ? fs.FamilyName : et.get_Parameter(
                            BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString() ?? "";

                        return familyName?.IndexOf(FamilyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               et.Name.IndexOf(FamilyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                }

                // Limit the number of results
                if (Limit.HasValue && Limit.Value > 0)
                {
                    filteredElements = filteredElements.Take(Limit.Value);
                }

                // Convert to FamilyTypeInfo list
                ResultFamilyTypes = filteredElements.Select(et =>
                {
                    string familyName;
                    if (et is FamilySymbol fs)
                    {
                        familyName = fs.FamilyName;
                    }
                    else
                    {
                        Parameter param = et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                        familyName = param?.AsString() ?? et.GetType().Name.Replace("Type", "");
                    }
                    return new FamilyTypeInfo
                    {
                        FamilyTypeId = et.Id.GetValue(),
                        UniqueId = et.UniqueId,
                        FamilyName = familyName,
                        TypeName = et.Name,
                        Category = et.Category?.Name
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                // ResultFamilyTypes was left null, so the caller received null and read it
                // as "this project has no family types".
                ErrorMessage = "Failed to retrieve family types: " + ex.Message;
                Diagnostics.Report("Error", ErrorMessage);
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "GetAvailableFamilyTypes";
        }
    }
}
