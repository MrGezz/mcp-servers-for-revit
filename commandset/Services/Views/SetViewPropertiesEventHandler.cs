using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    public class SetViewPropertiesEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;


        public int ViewId { get; private set; }
        public JObject Properties { get; private set; }

        public AIResult<bool> Result { get; private set; }

        public void SetParameters(int viewId, JObject properties)
        {
            ViewId = viewId;
            Properties = properties;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Set View Properties"))
                {
                    trans.Start();

                    View view = doc.GetElement(ElementIdFactory.Create(ViewId)) as View;
                    if (view == null)
                    {
                        Result = new AIResult<bool> { Success = false, Message = $"View with ID {ViewId} not found" };
                        return;
                    }

                    if (Properties == null)
                    {
                        Result = new AIResult<bool> { Success = false, Message = "No properties provided" };
                        return;
                    }

                    List<string> warnings = new List<string>();

                    if (Properties["scale"] != null)
                    {
                        int scaleVal = Properties["scale"].Value<int>();
                        try
                        {
                            view.Scale = scaleVal;
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                        {
                            // Scale is read-only when controlled by a view template; surface as warning.
                            warnings.Add("Scale not changed: controlled by view template");
                        }
                    }

                    if (Properties["detailLevel"] != null)
                    {
                        string dl = Properties["detailLevel"].Value<string>().ToLowerInvariant();
                        switch (dl)
                        {
                            case "coarse":
                                view.DetailLevel = ViewDetailLevel.Coarse;
                                break;
                            case "medium":
                                view.DetailLevel = ViewDetailLevel.Medium;
                                break;
                            case "fine":
                                view.DetailLevel = ViewDetailLevel.Fine;
                                break;
                        }
                    }

                    if (Properties["displayStyle"] != null)
                    {
                        string ds = Properties["displayStyle"].Value<string>().ToLowerInvariant();
                        int styleValue = 0;
                        switch (ds)
                        {
                            case "wireframe": styleValue = 0; break;
                            case "hidden":
                            case "hiddenline": styleValue = 1; break;
                            case "shaded":
                            case "shading": styleValue = 2; break;
                            case "consistent_colors": styleValue = 3; break;
                            case "realistic": styleValue = 4; break;
                        }
#if REVIT2026_OR_GREATER
                        view.DisplayStyle = (DisplayStyle)styleValue;
#else
                        // R20-R25: DisplayStyle property exists on View across the full span.
                        view.DisplayStyle = (DisplayStyle)styleValue;
#endif
                    }

                    if (Properties["templateId"] != null)
                    {
                        int templateIdVal = Properties["templateId"].Value<int>();
                        ElementId templateId = ElementIdFactory.Create(templateIdVal);
                        View templateView = doc.GetElement(templateId) as View;
                        if (templateView != null && templateView.IsTemplate)
                        {
                            view.ViewTemplateId = templateId;
                        }
                    }

                    if (Properties["cropBox"] != null)
                    {
                        JObject cropBox = Properties["cropBox"] as JObject;
                        if (cropBox != null)
                        {
                            double minX = cropBox["minX"]?.Value<double>() ?? 0;
                            double minY = cropBox["minY"]?.Value<double>() ?? 0;
                            double maxX = cropBox["maxX"]?.Value<double>() ?? 10;
                            double maxY = cropBox["maxY"]?.Value<double>() ?? 10;

                            view.CropBox = new BoundingBoxXYZ
                            {
                                Min = new XYZ(minX / 304.8, minY / 304.8, -10),
                                Max = new XYZ(maxX / 304.8, maxY / 304.8, 10)
                            };
                            view.CropBoxActive = true;
                            view.CropBoxVisible = true;
                        }
                    }

                    trans.Commit();

                    string message = "View properties updated successfully";
                    if (warnings.Count > 0)
                        message += ". Warnings: " + string.Join("; ", warnings);

                    Result = new AIResult<bool>
                    {
                        Success = true,
                        Message = message,
                        Response = true
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool>
                {
                    Success = false,
                    Message = $"Error setting view properties: {ex.Message}",
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

        public string GetName() => "Set View Properties";
    }
}
