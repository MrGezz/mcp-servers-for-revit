using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCurrentViewElementsEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Default model category list
        private readonly List<string> _defaultModelCategories = new List<string>
        {
            "OST_Walls",
            "OST_Doors",
            "OST_Windows",
            "OST_Furniture",
            "OST_Columns",
            "OST_Floors",
            "OST_Roofs",
            "OST_Stairs",
            "OST_StructuralFraming",
            "OST_Ceilings",
            "OST_MEPSpaces",
            "OST_Rooms"
        };
        // Default annotation category list
        private readonly List<string> _defaultAnnotationCategories = new List<string>
        {
            "OST_Dimensions",
            "OST_TextNotes",
            "OST_GenericAnnotation",
            "OST_WallTags",
            "OST_DoorTags",
            "OST_WindowTags",
            "OST_RoomTags",
            "OST_AreaTags",
            "OST_SpaceTags",
            "OST_ViewportLabels",
            "OST_TitleBlocks"
        };

        // Query parameters
        private List<string> _modelCategoryList;
        private List<string> _annotationCategoryList;
        private bool _includeHidden;
        private int _limit;

        // Execution result
        public ViewElementsResultWithWarning ResultInfo { get; private set; }

        // State synchronisation object
        public bool TaskCompleted { get; private set; }

        // Set query parameters; throws ArgumentException immediately if any category name is
        // not a recognised BuiltInCategory member, so the MCP caller receives an explicit
        // error rather than a silent empty result.
        public void SetQueryParameters(List<string> modelCategoryList, List<string> annotationCategoryList, bool includeHidden, int limit)
        {
            // Collect all requested names and validate them before raising the external event.
            List<string> requested = new List<string>();
            if (modelCategoryList != null) requested.AddRange(modelCategoryList);
            if (annotationCategoryList != null) requested.AddRange(annotationCategoryList);
            foreach (string name in requested)
            {
                if (!Enum.IsDefined(typeof(BuiltInCategory), name))
                {
                    string near = FindNearMatch(name);
                    string hint = near != null ? $" Did you mean '{near}'?" : string.Empty;
                    throw new ArgumentException($"Unknown BuiltInCategory name '{name}'.{hint}");
                }
            }
            _modelCategoryList = modelCategoryList;
            _annotationCategoryList = annotationCategoryList;
            _includeHidden = includeHidden;
            _limit = limit;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Implements IWaitableExternalEventHandler
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
                var activeView = doc.ActiveView;


                // Merge all categories
                List<string> allCategories = new List<string>();
                if (_modelCategoryList == null && _annotationCategoryList == null)
                {
                    allCategories.AddRange(_defaultModelCategories);
                    allCategories.AddRange(_defaultAnnotationCategories);
                }
                else
                {
                    allCategories.AddRange(_modelCategoryList ?? new List<string>());
                    allCategories.AddRange(_annotationCategoryList ?? new List<string>());
                }

                // Get all elements in the current view
                using var collector = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType();

                // Get all elements
                IList<Element> elements = collector.ToElements();

                // Filter by category
                // All names were validated in SetQueryParameters so Enum.TryParse is expected to succeed for every entry.
                int resolvedCategoryCount = 0;
                if (allCategories.Count > 0)
                {
                    List<BuiltInCategory> builtInCategories = new List<BuiltInCategory>();
                    foreach (string categoryName in allCategories)
                    {
                        if (Enum.TryParse(categoryName, out BuiltInCategory category))
                        {
                            builtInCategories.Add(category);
                        }
                    }
                    resolvedCategoryCount = builtInCategories.Count;
                    if (builtInCategories.Count > 0)
                    {
                        ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(builtInCategories);
                        using (var catCollector = new FilteredElementCollector(doc, activeView.Id))
                        {
                            elements = catCollector
                                .WhereElementIsNotElementType()
                                .WherePasses(categoryFilter)
                                .ToElements();
                        }
                    }
                }

                // Filter out hidden elements
                if (!_includeHidden)
                {
                    elements = elements.Where(e => !e.IsHidden(activeView)).ToList();
                }

                // Limit the number of returned elements
                if (_limit > 0 && elements.Count > _limit)
                {
                    elements = elements.Take(_limit).ToList();
                }

                // Build the result
                var elementInfos = elements.Select(e => new ElementInfo
                {
                    Id = e.Id.GetValue(),
                    UniqueId = e.UniqueId,
                    Name = e.Name,
                    Category = e.Category?.Name ?? "unknown",
                    Properties = GetElementProperties(e)
                }).ToList();

                // When a category filter was applied and resolved but found nothing, include
                // an explicit warning so the caller can distinguish an empty model from a bad request.
                string warning = (allCategories.Count > 0 && resolvedCategoryCount > 0 && elementInfos.Count == 0)
                    ? $"The requested categor{(allCategories.Count == 1 ? "y" : "ies")} resolved correctly ({string.Join(", ", allCategories)}) but matched no elements in the current view."
                    : null;

                ResultInfo = new ViewElementsResultWithWarning
                {
                    ViewId = activeView.Id.GetValue(),
                    ViewName = activeView.Name,
                    TotalElementsInView = GetTotalElementCount(doc, activeView.Id),
                    FilteredElementCount = elementInfos.Count,
                    Elements = elementInfos,
                    Warning = warning
                };
            }
            catch (Exception ex)
            {
                Diagnostics.Report("error", ex.Message);

                // ResultInfo was previously left NULL here, and the command returned that
                // null to the caller with no error - a failure that looked like an empty
                // answer. Report it.
                ResultInfo = new ViewElementsResultWithWarning
                {
                    FilteredElementCount = 0,
                    Elements = new List<ElementInfo>(),
                    Warning = "Failed to read the current view: " + ex.Message,
                    Ok = false
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private static int GetTotalElementCount(Document doc, ElementId viewId)
        {
            using var counter = new FilteredElementCollector(doc, viewId);
            return counter.GetElementCount();
        }

        private Dictionary<string, string> GetElementProperties(Element element)
        {
            var properties = new Dictionary<string, string>();

            // Location in MILLIMETRES, with the unit in the key. These were written
            // as raw internal feet ("LocationX": "12.34") under a tool that promises
            // mm everywhere, which is exactly the kind of number an AI then feeds
            // back into create_wall as if it were millimetres.
            const double FT_TO_MM = 304.8;
            string Mm(XYZ p) => $"{p.X * FT_TO_MM:F1}, {p.Y * FT_TO_MM:F1}, {p.Z * FT_TO_MM:F1}";

            if (element.Location != null)
            {
                if (element.Location is LocationPoint locationPoint)
                {
                    properties.Add("LocationMm", Mm(locationPoint.Point));
                }
                else if (element.Location is LocationCurve locationCurve)
                {
                    var curve = locationCurve.Curve;
                    properties.Add("StartMm", Mm(curve.GetEndPoint(0)));
                    properties.Add("EndMm", Mm(curve.GetEndPoint(1)));
                    properties.Add("LengthMm", (curve.Length * FT_TO_MM).ToString("F1"));
                }
            }

            // Common parameters. Family and Type are resolved to NAMES (they were
            // emitted as raw ElementIds: "Family": "49559"), and doubles use the
            // display string so the unit travels with the number ("3000 mm").
            if (element is FamilyInstance fi)
            {
                if (!string.IsNullOrEmpty(fi.Symbol?.FamilyName)) properties["Family"] = fi.Symbol.FamilyName;
                if (!string.IsNullOrEmpty(fi.Symbol?.Name)) properties["Type"] = fi.Symbol.Name;
            }
            else
            {
                var typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeElem = element.Document.GetElement(typeId);
                    if (typeElem != null)
                    {
                        properties["Type"] = typeElem.Name;
                        string family = typeElem.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString();
                        if (!string.IsNullOrEmpty(family)) properties["Family"] = family;
                    }
                }
            }

            var commonParams = new[] { "Comments", "Mark", "Level" };
            foreach (var paramName in commonParams)
            {
                Parameter param = element.LookupParameter(paramName);
                if (param == null || !param.HasValue) continue;
                string value = null;
                switch (param.StorageType)
                {
                    case StorageType.String: value = param.AsString(); break;
                    case StorageType.Double: value = param.AsValueString() ?? (param.AsDouble() * FT_TO_MM).ToString("F1") + " mm"; break;
                    case StorageType.Integer: value = param.AsValueString() ?? param.AsInteger().ToString(); break;
                    case StorageType.ElementId:
                        var referenced = element.Document.GetElement(param.AsElementId());
                        value = referenced?.Name ?? param.AsElementId().GetValue().ToString();
                        break;
                }
                if (!string.IsNullOrEmpty(value)) properties[paramName] = value;
            }

            return properties;
        }

        // Compute the Levenshtein edit distance between two strings using a two-row rolling array.
        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                }
                int[] tmp = prev; prev = curr; curr = tmp;
            }
            return prev[b.Length];
        }

        // Return the closest BuiltInCategory name to the supplied unrecognised string, or null
        // when no candidate is within a plausible typo distance (five edits or fewer).
        private static string FindNearMatch(string bad)
        {
            string[] allNames = Enum.GetNames(typeof(BuiltInCategory));
            int bestDist = int.MaxValue;
            string bestName = null;
            foreach (string name in allNames)
            {
                int dist = LevenshteinDistance(bad, name);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestName = name;
                }
            }
            return bestDist <= 5 ? bestName : null;
        }

        public string GetName()
        {
            return "Get Current View Elements";
        }
    }

    // ViewElementsResult extended with an optional warning message.
    // A non-null Warning means the category filter resolved but matched nothing in the view;
    // an unrecognised category name is surfaced as an ArgumentException before any result is built.
    public class ViewElementsResultWithWarning : ViewElementsResult
    {
        public string Warning { get; set; }
        // Serialised as "ok": false so fromRevit (reply.ts) surfaces exceptions as isError:true.
        // The attribute matters: without it Newtonsoft emits "Ok", which reply.ts did not check.
        [Newtonsoft.Json.JsonProperty("ok")]
        public bool Ok { get; set; } = true;
    }
}
