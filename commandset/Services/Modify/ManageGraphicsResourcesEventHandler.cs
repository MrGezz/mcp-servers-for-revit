using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Modify
{
    public class ManageGraphicsResourcesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; private set; }
        public string ResourceName { get; private set; }
        public JObject Properties { get; private set; }

        public AIResult<bool> Result { get; private set; }
        private List<string> _warnings = new List<string>();

        // How many properties were actually applied. Success is reported only when
        // this is non-zero, so a call that matched nothing cannot claim to have
        // 'managed' the resource.
        private int _applied;

        public void SetParameters(string action, string name, JObject properties)
        {
            Action = action;
            ResourceName = name;
            Properties = properties;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                using (Transaction trans = new Transaction(doc, "Manage Graphics Resources"))
                {
                    trans.Start();

                    switch (Action.ToLowerInvariant())
                    {
                        case "line_style":
                            HandleLineStyle();
                            break;
                        case "fill_pattern":
                            HandleFillPattern();
                            break;
                        default:
                            Result = new AIResult<bool> { Success = false, Message = $"Unknown action: {Action}. Use 'line_style' or 'fill_pattern'" };
                            return;
                    }

                    trans.Commit();

                    // The handler used to report unconditional success while _warnings was
                    // never read, so "no category on that style" and "nothing in Properties
                    // to apply" both came back as "managed successfully". A tool that says
                    // it did something it did not do is worse than one that fails.
                    if (_applied == 0)
                    {
                        Result = new AIResult<bool>
                        {
                            Success = false,
                            Message =
                                $"Graphics resource '{Action}': nothing was changed. " +
                                (_warnings.Count > 0
                                    ? string.Join(" ", _warnings)
                                    : "No recognised property was supplied. 'line_style' accepts lineWeight, " +
                                      "color and linePattern."),
                            Response = false
                        };
                    }
                    else
                    {
                        Result = new AIResult<bool>
                        {
                            Success = true,
                            Message =
                                $"Graphics resource '{Action}': {_applied} propert" +
                                (_applied == 1 ? "y" : "ies") + " applied." +
                                (_warnings.Count > 0 ? " " + string.Join(" ", _warnings) : string.Empty),
                            Response = true
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool>
                {
                    Success = false,
                    Message = $"Error managing graphics resources: {ex.Message}",
                    Response = false
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void HandleLineStyle()
        {
            GraphicsStyle existingStyle = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .FirstOrDefault(gs => gs.Name == ResourceName);

            // Each miss below used to be silent. They are recorded now, and _applied is
            // what decides whether this handler reports success at all.
            if (existingStyle == null)
            {
                _warnings.Add($"No GraphicsStyle named '{ResourceName}' exists in this document.");
                return;
            }

            if (Properties == null)
            {
                _warnings.Add("No properties were supplied, so there was nothing to apply.");
                return;
            }

            // Category.SetLineWeight / .LineColor / .SetLinePatternId are present and
            // identical on Revit 2022-2027.  No version guard is needed.
            Category category = existingStyle.GraphicsStyleCategory;
            if (category == null)
            {
                _warnings.Add($"GraphicsStyle '{ResourceName}' has no associated category; line style properties cannot be set.");
                return;
            }

            if (Properties["lineWeight"] != null)
            {
                int lineWeight = Properties["lineWeight"].Value<int>();
                category.SetLineWeight(lineWeight, GraphicsStyleType.Projection);
                _applied++;
            }

            if (Properties["color"] != null)
            {
                JObject colorObj = Properties["color"] as JObject;
                if (colorObj != null)
                {
                    byte r = (byte)(colorObj["r"]?.Value<int>() ?? 0);
                    byte g = (byte)(colorObj["g"]?.Value<int>() ?? 0);
                    byte b = (byte)(colorObj["b"]?.Value<int>() ?? 0);
                    category.LineColor = new Color(r, g, b);
                    _applied++;
                }
                else
                {
                    _warnings.Add("'color' was supplied but is not an object with r, g and b.");
                }
            }

            if (Properties["linePattern"] != null)
            {
                string patternName = Properties["linePattern"].Value<string>();
                LinePatternElement pattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault(lp => lp.Name == patternName);

                if (pattern != null)
                {
                    category.SetLinePatternId(pattern.Id, GraphicsStyleType.Projection);
                    _applied++;
                }
                else
                {
                    _warnings.Add($"No line pattern named '{patternName}' exists in this document.");
                }
            }
        }

        private void HandleFillPattern()
        {
            FillPatternElement existingPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.Name == ResourceName);

            if (existingPattern == null)
            {
                _warnings.Add($"No FillPatternElement named '{ResourceName}' exists in this document.");
                return;
            }

            if (Properties == null || Properties["color"] == null)
            {
                // 'color' is the only property this action ever supported, and it is not
                // achievable (see below). Saying so beats returning "managed successfully"
                // for an action that has nothing it can do.
                _warnings.Add(
                    "The 'fill_pattern' action supports no settable property: the only one it ever " +
                    "accepted was 'color', which is a view override rather than a property of the " +
                    "pattern element. Use a view override instead.");
                return;
            }

            // FillPattern has no Color property on any Revit version (2022-2027).
            // Color is a view-level override, not a property of the pattern object.
            throw new NotSupportedException(
                "FillPattern.Color does not exist on any supported Revit version (2022-2027). " +
                "Fill pattern color is a view override, not a property of the pattern element. " +
                "Use OverrideGraphicSettings.SetSurfaceForegroundPatternColor or " +
                "SetCutForegroundPatternColor on the target view instead.");
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName() => "Manage Graphics Resources";
    }
}
