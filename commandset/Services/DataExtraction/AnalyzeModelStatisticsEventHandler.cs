using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.DataExtraction;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class AnalyzeModelStatisticsEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private bool _includeDetailedTypes;

        public AnalyzeModelStatisticsResult ResultInfo { get; private set; }
        public bool TaskCompleted { get; private set; }

        public void SetParameters(bool includeDetailedTypes = false)
        {
            _includeDetailedTypes = includeDetailedTypes;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get project name
                string projectName = doc.Title;

                // Count total elements
                int totalElements;
                using (var coll = new FilteredElementCollector(doc))
                    totalElements = coll.WhereElementIsNotElementType().GetElementCount();

                // Count total types
                int totalTypes;
                using (var coll = new FilteredElementCollector(doc))
                    totalTypes = coll.WhereElementIsElementType().GetElementCount();

                // Count views
                int totalViews;
                using (var coll = new FilteredElementCollector(doc))
                    totalViews = coll.OfClass(typeof(View)).Where(v => !(v as View).IsTemplate).Count();

                // Count sheets
                int totalSheets;
                using (var coll = new FilteredElementCollector(doc))
                    totalSheets = coll.OfClass(typeof(ViewSheet)).GetElementCount();

                // Analyze by category
                var categoryStats = new Dictionary<string, CategoryStatistics>();
                var categoryTypeNames = new Dictionary<string, HashSet<string>>();
                var categoryFamilyNames = new Dictionary<string, HashSet<string>>();
                var familyNames = new HashSet<string>();

                IList<Element> elements;
                using (var coll = new FilteredElementCollector(doc))
                    elements = coll.WhereElementIsNotElementType().ToElements();

                foreach (Element elem in elements)
                {
                    if (elem.Category == null) continue;

                    string catName = elem.Category.Name;

                    if (!categoryStats.ContainsKey(catName))
                    {
                        categoryStats[catName] = new CategoryStatistics
                        {
                            CategoryName = catName
                        };
                        categoryTypeNames[catName] = new HashSet<string>();
                        categoryFamilyNames[catName] = new HashSet<string>();
                    }

                    categoryStats[catName].ElementCount++;

                    // Track type information
                    if (elem is FamilyInstance fi)
                    {
                        string familyName = fi.Symbol?.Family?.Name;
                        string typeName = fi.Symbol?.Name;

                        if (!string.IsNullOrEmpty(familyName))
                        {
                            familyNames.Add(familyName);
                            categoryFamilyNames[catName].Add(familyName);
                        }

                        if (!string.IsNullOrEmpty(typeName))
                        {
                            categoryTypeNames[catName].Add(typeName);
                        }

                        if (_includeDetailedTypes && !string.IsNullOrEmpty(typeName))
                        {
                            var existingType = categoryStats[catName].Types
                                .FirstOrDefault(t => t.TypeName == typeName && t.FamilyName == familyName);

                            if (existingType != null)
                            {
                                existingType.InstanceCount++;
                            }
                            else
                            {
                                categoryStats[catName].Types.Add(new TypeStatistics
                                {
                                    TypeName = typeName,
                                    FamilyName = familyName,
                                    InstanceCount = 1
                                });
                            }
                        }
                    }
                }

                // Set type and family counts from tracked sets (accurate regardless of includeDetailedTypes)
                foreach (var kvp in categoryStats)
                {
                    kvp.Value.TypeCount = categoryTypeNames[kvp.Key].Count;
                    kvp.Value.FamilyCount = categoryFamilyNames[kvp.Key].Count;
                }

                // Analyze by level
                var levelStats = new List<LevelStatistics>();
                List<Level> levels;
                using (var coll = new FilteredElementCollector(doc))
                    levels = coll.OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();

                foreach (Level level in levels)
                {
                    int elementCount;
                    using (var coll = new FilteredElementCollector(doc))
                        elementCount = coll.WhereElementIsNotElementType().Where(e => e.LevelId == level.Id).Count();

                    levelStats.Add(new LevelStatistics
                    {
                        LevelName = level.Name,
                        Elevation = level.Elevation * 304.8,
                        ElementCount = elementCount
                    });
                }

                ResultInfo = new AnalyzeModelStatisticsResult
                {
                    ProjectName = projectName,
                    TotalElements = totalElements,
                    TotalTypes = totalTypes,
                    TotalFamilies = familyNames.Count,
                    TotalViews = totalViews,
                    TotalSheets = totalSheets,
                    Categories = categoryStats.Values.OrderByDescending(c => c.ElementCount).ToList(),
                    Levels = levelStats,
                    Success = true,
                    Message = _includeDetailedTypes
                        ? $"Successfully analyzed model with {totalElements} elements across {categoryStats.Count} categories"
                        : $"Successfully analyzed model with {totalElements} elements across {categoryStats.Count} categories. Per-type breakdown omitted; pass includeDetailedTypes=true to include it."
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new AnalyzeModelStatisticsResult
                {
                    Success = false,
                    Message = $"Error analyzing model statistics: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Analyze Model Statistics";
        }
    }
}
