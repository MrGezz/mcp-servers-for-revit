using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class CreateSheetEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public List<SheetCreationInfo> CreatedInfo { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        private List<string> _warnings = new List<string>();

        public void SetParameters(List<SheetCreationInfo> data)
        {
            CreatedInfo = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var sheetIds = new List<int>();
                _warnings.Clear();

                foreach (var info in CreatedInfo)
                {
                    using (Transaction trans = new Transaction(doc, "Create Sheet"))
                    {
                        trans.Start();

                        ElementId titleBlockTypeId = ElementId.InvalidElementId;

                        if (info.TitleBlockTypeId > 0)
                        {
                            titleBlockTypeId = ElementIdFactory.Create(info.TitleBlockTypeId);
                            Element tbElem = doc.GetElement(titleBlockTypeId);
                            if (tbElem == null || !(tbElem is FamilySymbol))
                            {
                                _warnings.Add($"Title block type ID {info.TitleBlockTypeId} not found. Trying by name.");
                                titleBlockTypeId = ElementId.InvalidElementId;
                            }
                        }

                        if (titleBlockTypeId == ElementId.InvalidElementId && !string.IsNullOrEmpty(info.TitleBlockFamilyName))
                        {
                            FamilySymbol tbSymbol;
                            using (var collector = new FilteredElementCollector(doc))
                            {
                                tbSymbol = collector
                                    .OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs =>
                                    {
                                        if (fs.FamilyName != null && info.TitleBlockFamilyName != null &&
                                            fs.FamilyName.Equals(info.TitleBlockFamilyName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!string.IsNullOrEmpty(info.TitleBlockTypeName))
                                                return fs.Name != null && fs.Name.Equals(info.TitleBlockTypeName, StringComparison.OrdinalIgnoreCase);
                                            return true;
                                        }
                                        return false;
                                    });
                            }

                            if (tbSymbol != null)
                            {
                                titleBlockTypeId = tbSymbol.Id;
                            }
                            else
                            {
                                _warnings.Add($"Title block family '{info.TitleBlockFamilyName}' not found. Creating blank sheet.");
                            }
                        }

                        if (titleBlockTypeId == ElementId.InvalidElementId)
                        {
                            FamilySymbol defaultTb;
                            using (var collector = new FilteredElementCollector(doc))
                            {
                                defaultTb = collector
                                    .OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs =>
                                    {
                                        Category cat = fs.Category;
                                        return cat != null && cat.Id.GetIntValue() == (int)BuiltInCategory.OST_TitleBlocks;
                                    });
                            }

                            if (defaultTb != null)
                            {
                                titleBlockTypeId = defaultTb.Id;
                                _warnings.Add($"Using default title block type '{defaultTb.Name}'.");
                            }
                        }

                        ViewSheet sheet = ViewSheet.Create(doc, titleBlockTypeId);

                        if (sheet != null)
                        {
                            if (!string.IsNullOrEmpty(info.SheetNumber))
                            {
                                sheet.SheetNumber = info.SheetNumber;
                            }

                            if (!string.IsNullOrEmpty(info.SheetName))
                            {
                                sheet.Name = info.SheetName;
                            }

                            if (info.RevisionIds != null && info.RevisionIds.Count > 0)
                            {
                                foreach (int revId in info.RevisionIds)
                                {
                                    Element revElem = doc.GetElement(ElementIdFactory.Create(revId));
                                    if (revElem is Revision)
                                    {
                                        VersionCompat.AddRevisionToSheet(sheet, ElementIdFactory.Create(revId));
                                    }
                                }
                            }

                            foreach (var param in info.Parameters)
                            {
                                Parameter sheetParam = sheet.LookupParameter(param.Key);
                                if (sheetParam != null)
                                {
                                    SetParameterValue(sheetParam, param.Value);
                                }
                            }

                            sheetIds.Add(sheet.Id.GetIntValue());
                        }
                        else
                        {
                            _warnings.Add($"Failed to create sheet (sheet number: '{info.SheetNumber ?? "N/A"}', name: '{info.SheetName ?? "N/A"}'). ViewSheet.Create returned null.");
                        }

                        trans.Commit();
                    }
                }

                bool created = sheetIds.Count > 0;
                string message = created
                    ? $"Successfully created {sheetIds.Count} sheet(s)."
                    : "Nothing was created.";
                if (_warnings.Count > 0)
                {
                    message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
                }

                Result = new AIResult<List<int>>
                {
                    Success = created,
                    Message = message,
                    Response = sheetIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating sheet: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
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

        public string GetName() => "Create Sheet";
    }
}
