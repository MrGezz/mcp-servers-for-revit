using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Modify
{
    public class SetParametersEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private Document Doc => uiApp.ActiveUIDocument.Document;
        public int ElementId { get; private set; }
        public JObject ParameterValues { get; private set; }
        public AIResult<bool> Result { get; private set; }

        public void SetParameters(int elementId, JObject parameters)
        {
            ElementId = elementId;
            ParameterValues = parameters;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;
            try
            {
                var element = Doc.GetElement(ElementIdFactory.Create(ElementId));
                if (element == null)
                {
                    Result = new AIResult<bool> { Success = false, Message = $"Element {ElementId} not found" };
                    return;
                }
                var notFound = new List<string>();
                var readOnly = new List<string>();
                var rejected = new List<string>();
                int setCount = 0;

                using (var trans = new Transaction(Doc, "Set Parameters"))
                {
                    trans.Start();
                    foreach (var prop in ParameterValues.Properties())
                    {
                        var param = element.LookupParameter(prop.Name);
                        if (param == null)
                        {
                            param = LookupBuiltInParameter(element, prop.Name);
                        }
                        if (param == null)
                        {
                            notFound.Add(prop.Name);
                            continue;
                        }
                        if (param.IsReadOnly)
                        {
                            readOnly.Add(prop.Name);
                            continue;
                        }
                        var value = prop.Value;
                        bool wrote;
                        if (value.Type == JTokenType.String)
                            wrote = param.Set(value.Value<string>());
                        else if (value.Type == JTokenType.Integer)
                            wrote = param.Set(value.Value<int>());
                        else if (value.Type == JTokenType.Float)
                        {
                            double d = value.Value<double>();
#if REVIT2023_OR_GREATER
                            bool isLength = param.Definition.GetDataType().Equals(SpecTypeId.Length);
#else
                            bool isLength = param.Definition.ParameterType == ParameterType.Length;
#endif
                            if (isLength)
                                d /= 304.8;
                            wrote = param.Set(d);
                        }
                        else if (value.Type == JTokenType.Boolean)
                            wrote = param.Set(value.Value<bool>() ? 1 : 0);
                        else
                        {
                            // null / array / object: nothing to write, and previously this
                            // still counted as "set".
                            rejected.Add($"{prop.Name} (unsupported value type {value.Type})");
                            continue;
                        }
                        // Parameter.Set returns false when Revit refused the value (wrong
                        // storage type, invalid for this element); count only real writes.
                        if (wrote) setCount++;
                        else rejected.Add($"{prop.Name} (Revit rejected the value)");
                    }
                    trans.Commit();
                }

                var skipped = new List<string>();
                foreach (var n in notFound) skipped.Add($"{n} (not found)");
                foreach (var r in readOnly) skipped.Add($"{r} (read-only)");
                foreach (var x in rejected) skipped.Add(x);

                if (setCount == 0 && skipped.Count > 0)
                {
                    Result = new AIResult<bool>
                    {
                        Success = false,
                        Message = $"No parameters were set. Skipped: {string.Join(", ", skipped)}"
                    };
                }
                else if (skipped.Count > 0)
                {
                    Result = new AIResult<bool>
                    {
                        Success = true,
                        Response = true,
                        Message = $"Warning: some parameters were skipped: {string.Join(", ", skipped)}"
                    };
                }
                else
                {
                    Result = new AIResult<bool> { Success = true, Response = true };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<bool> { Success = false, Message = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private Parameter LookupBuiltInParameter(Element element, string name)
        {
            foreach (Parameter param in element.Parameters)
            {
                var def = param.Definition;
                if (def != null)
                {
                    if (def.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return param;
                    var builtIn = def as InternalDefinition;
                    if (builtIn != null && builtIn.BuiltInParameter.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                        return param;
                }
            }
            return null;
        }

        public bool WaitForCompletion(int timeout = 10000)
        {
            return _resetEvent.WaitOne(timeout);
        }

        public string GetName() => "Set Parameters";
    }
}
