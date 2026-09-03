using RevitMCPCommandSet.Localization;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateScheduleEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public List<ScheduleCreationInfo> CreatedInfo { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        private List<string> _warnings = new List<string>();

        public void SetParameters(List<ScheduleCreationInfo> data)
        {
            CreatedInfo = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var scheduleIds = new List<int>();
                _warnings.Clear();

                foreach (var info in CreatedInfo)
                {
                    using (Transaction trans = new Transaction(doc, "Create Schedule"))
                    {
                        trans.Start();

                        ViewSchedule schedule = null;
                        string scheduleType = info.Type?.ToLowerInvariant() ?? "regular";

                        // Not a switch any more: the accepted values include LOCALIZED
                        // aliases, and those now live in the locale catalogue as data rather
                        // than as literals in control flow. Deleting them outright would have
                        // broken callers on a non-English Revit. See RevitUiTerms.
                        if (RevitUiTerms.Matches(RevitUiTerms.ScheduleMaterial, scheduleType) ||
                            scheduleType == "materialtakeoff")
                        {
                            schedule = CreateMaterialTakeoff(info);
                        }
                        else if (RevitUiTerms.Matches(RevitUiTerms.ScheduleKey, scheduleType) ||
                                 scheduleType == "keynote")
                        {
                            schedule = CreateKeySchedule(info);
                        }
                        else if (RevitUiTerms.Matches(RevitUiTerms.ScheduleViewList, scheduleType) ||
                                 scheduleType == "viewlist")
                        {
                            schedule = CreateBuiltInSchedule(BuiltInCategory.OST_Views, "View List");
                        }
                        else if (RevitUiTerms.Matches(RevitUiTerms.ScheduleSheetList, scheduleType) ||
                                 scheduleType == "sheetlist")
                        {
                            schedule = CreateBuiltInSchedule(BuiltInCategory.OST_Sheets, "Sheet List");
                        }
                        else if (RevitUiTerms.Matches(RevitUiTerms.ScheduleRevision, scheduleType))
                        {
                            schedule = CreateBuiltInSchedule(BuiltInCategory.OST_Revisions, "Revision Schedule");
                        }
                        else
                        {
                            // "regular" / "general", and anything unrecognised.
                            schedule = CreateRegularSchedule(info);
                        }

                        if (schedule != null)
                        {
                            if (!string.IsNullOrEmpty(info.Name))
                            {
                                schedule.Name = info.Name;
                            }

                            if (info.ShowTitle.HasValue)
                            {
                                // ShowTitle is a property of ScheduleDefinition, identical on 2022-2027.
                                schedule.Definition.ShowTitle = info.ShowTitle.Value;
                            }

                            if (info.ShowHeaders.HasValue)
                            {
                                // ShowHeaders is on ScheduleDefinition, not ViewSchedule; exists on all versions.
                                VersionCompat.SetScheduleShowHeaders(schedule, info.ShowHeaders.Value);
                            }

                            if (info.ShowGridLines.HasValue)
                            {
                                // ShowGridLines is on ScheduleDefinition, not ViewSchedule; exists on all versions.
                                VersionCompat.SetScheduleShowGridLines(schedule, info.ShowGridLines.Value);
                            }

                            if (info.ShowOutlines.HasValue)
                            {
                                // ShowOutlines does not exist on ViewSchedule, ScheduleDefinition, or any
                                // Revit API type across versions 2022-2027. It cannot be set via the API.
                                // Adjust it manually in the Revit schedule properties dialog after creation.
                                _warnings.Add(
                                    "ShowOutlines cannot be set: the property does not exist on ViewSchedule " +
                                    "or ScheduleDefinition in any Revit version (2022-2027). " +
                                    "Set it manually via the schedule's Properties dialog in Revit.");
                            }

                            if (!string.IsNullOrEmpty(info.TemplateId) && int.TryParse(info.TemplateId, out int templateIntId))
                            {
                                ElementId templateId = ElementIdFactory.Create(templateIntId);
                                View templateView = doc.GetElement(templateId) as View;
                                if (templateView != null && templateView.IsTemplate)
                                {
                                    schedule.ViewTemplateId = templateId;
                                }
                            }

                            foreach (var param in info.Parameters)
                            {
                                Parameter schedParam = schedule.LookupParameter(param.Key);
                                if (schedParam != null)
                                {
                                    SetParameterValue(schedParam, param.Value);
                                }
                            }

                            scheduleIds.Add(schedule.Id.GetIntValue());
                        }

                        trans.Commit();
                    }
                }

                bool created = scheduleIds.Count > 0;
                string message = created
                    ? $"Successfully created {scheduleIds.Count} schedule(s)."
                    : "Nothing was created.";
                if (_warnings.Count > 0)
                {
                    message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
                }

                Result = new AIResult<List<int>>
                {
                    Success = created,
                    Message = message,
                    Response = scheduleIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating schedule: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ViewSchedule CreateRegularSchedule(ScheduleCreationInfo info)
        {
            ElementId categoryId = FindCategoryId(info);
            if (categoryId == ElementId.InvalidElementId)
            {
                _warnings.Add($"Could not resolve category for schedule. Schedule not created.");
                return null;
            }

            ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, categoryId);
            return schedule;
        }

        private ViewSchedule CreateMaterialTakeoff(ScheduleCreationInfo info)
        {
            ElementId categoryId = FindCategoryId(info);
            if (categoryId == ElementId.InvalidElementId) return null;

            ViewSchedule schedule = ViewSchedule.CreateMaterialTakeoff(doc, categoryId);
            return schedule;
        }

        private ViewSchedule CreateKeySchedule(ScheduleCreationInfo info)
        {
            ElementId categoryId = FindCategoryId(info);
            if (categoryId == ElementId.InvalidElementId) return null;

            ViewSchedule schedule = ViewSchedule.CreateKeySchedule(doc, categoryId);
            return schedule;
        }

        private ViewSchedule CreateBuiltInSchedule(BuiltInCategory category, string defaultName)
        {
            ElementId catId = ElementIdFactory.Create((int)category);
            ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, catId);
            if (schedule != null && !string.IsNullOrEmpty(defaultName))
            {
                // Naming can legitimately fail (a schedule of that name already exists),
                // but swallowing the reason left the caller with a differently-named
                // schedule and no explanation.
                try
                {
                    schedule.Name = defaultName;
                }
                catch (Exception ex)
                {
                    _warnings.Add($"Could not name the schedule '{defaultName}': {ex.Message}. " +
                                  $"It was left as '{schedule.Name}'.");
                }
            }
            return schedule;
        }

        private ElementId FindCategoryId(ScheduleCreationInfo info)
        {
            if (info.CategoryId > 0)
            {
                ElementId catId = ElementIdFactory.Create(info.CategoryId);
                Category cat = Category.GetCategory(doc, catId);
                if (cat != null) return catId;
            }

            if (!string.IsNullOrEmpty(info.CategoryName))
            {
                BuiltInCategory bic;
                string catName = info.CategoryName.Replace(" ", "").Replace("-", "");
                if (Enum.TryParse(catName, true, out bic))
                {
                    return ElementIdFactory.Create((int)bic);
                }

                Category matchedCat = null;
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c.Name != null && c.Name.Equals(info.CategoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedCat = c;
                        break;
                    }
                }

                if (matchedCat != null) return matchedCat.Id;

                _warnings.Add($"Category '{info.CategoryName}' not found.");
            }

            _warnings.Add($"No category specified. Defaulting to Walls.");
            return ElementIdFactory.Create((int)BuiltInCategory.OST_Walls);
        }

        private void SetParameterValue(Parameter param, object value)
        {
            if (value == null) return;

            switch (param.StorageType)
            {
                case StorageType.Integer:
                    if (value is long l) param.Set((int)l);
                    else if (value is int i) param.Set(i);
                    else if (value is bool b) param.Set(b ? 1 : 0);
                    break;
                case StorageType.Double:
                    if (value is double d) param.Set(d);
                    else if (value is long ld) param.Set((double)ld);
                    break;
                case StorageType.String:
                    param.Set(value.ToString());
                    break;
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Create Schedule";
    }
}
