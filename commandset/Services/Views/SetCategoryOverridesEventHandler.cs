using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class SetCategoryOverridesEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public int ViewId { get; private set; }
        public int CategoryId { get; private set; }
        public JObject Overrides { get; private set; }

        public AIResult<bool> Result { get; private set; }

        public void SetParameters(int viewId, int categoryId, JObject overrides)
        {
            ViewId = viewId;
            CategoryId = categoryId;
            Overrides = overrides;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Set Category Overrides"))
                {
                    trans.Start();

                    View view = doc.GetElement(ElementIdFactory.Create(ViewId)) as View;
                    if (view == null)
                    {
                        Result = new AIResult<bool> { Success = false, Message = $"View with ID {ViewId} not found" };
                        return;
                    }

                    ElementId catId = ElementIdFactory.Create(CategoryId);
                    OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();

                    if (Overrides != null)
                    {
                        if (Overrides["color"] != null)
                        {
                            JObject colorObj = Overrides["color"] as JObject;
                            if (colorObj != null)
                            {
                                byte r = (byte)(colorObj["r"]?.Value<int>() ?? 0);
                                byte g = (byte)(colorObj["g"]?.Value<int>() ?? 0);
                                byte b = (byte)(colorObj["b"]?.Value<int>() ?? 0);
                                Color color = new Color(r, g, b);
                                overrideSettings.SetProjectionLineColor(color);
                                overrideSettings.SetCutLineColor(color);
                            }
                        }

                        if (Overrides["lineWeight"] != null)
                        {
                            int lw = Overrides["lineWeight"].Value<int>();
                            overrideSettings.SetProjectionLineWeight(lw);
                            overrideSettings.SetCutLineWeight(lw);
                        }

                        if (Overrides["fillPattern"] != null)
                        {
                            string fpName = Overrides["fillPattern"].Value<string>();
                            FillPatternElement fpElem;
                            using (var collector = new FilteredElementCollector(doc))
                            {
                                fpElem = collector
                                    .OfClass(typeof(FillPatternElement))
                                    .Cast<FillPatternElement>()
                                    .FirstOrDefault(fp => fp.Name == fpName);
                            }
                            if (fpElem != null)
                            {
                                overrideSettings.SetSurfaceForegroundPatternId(fpElem.Id);
                                overrideSettings.SetSurfaceForegroundPatternVisible(true);
                            }
                            else
                            {
                                Result = new AIResult<bool> { Success = false, Message = $"Fill pattern '{fpName}' not found in document" };
                                trans.RollBack();
                                return;
                            }
                        }

                        if (Overrides["halftone"] != null)
                        {
                            bool halftone = Overrides["halftone"].Value<bool>();
                            overrideSettings.SetHalftone(halftone);
                        }

                        if (Overrides["transparency"] != null)
                        {
                            int transp = Overrides["transparency"].Value<int>();
                            overrideSettings.SetSurfaceTransparency(transp);
                        }
                    }

                    view.SetCategoryOverrides(catId, overrideSettings);

                    trans.Commit();

                    Result = new AIResult<bool>
                    {
                        Success = true,
                        Message = "Category overrides applied successfully",
                        Response = true
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool>
                {
                    Success = false,
                    Message = $"Error setting category overrides: {ex.Message}",
                    Response = false
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

        public string GetName() => "Set Category Overrides";
    }
}
