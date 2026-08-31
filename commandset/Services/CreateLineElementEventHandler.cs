using RevitMCPCommandSet.Utils;
using Autodesk.Revit.DB.Mechanical;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateLineElementEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;
        /// <summary>
        /// Event wait handle
        /// </summary>
        /// <summary>
        /// Creation data (input data)
        /// </summary>
        public List<LineElement> CreatedInfo { get; private set; }
        /// <summary>
        /// Execution result (output data)
        /// </summary>
        public AIResult<List<int>> Result { get; private set; }
        private List<string> _warnings = new List<string>();

        public string _wallName = "Generic - ";
        public string _ductName = "Rectangular Duct - ";

        /// <summary>
        /// Set creation parameters
        /// </summary>
        public void SetParameters(List<LineElement> data)
        {
            CreatedInfo = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();
                foreach (var data in CreatedInfo)
                {
                    int requestedTypeId = data.TypeId;

                    // Step 0: Get element category
                    BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
                    Enum.TryParse(data.Category.Replace(".", ""), true, out builtInCategory);

                    // Step 1: Get level and offset
                    Level baseLevel = null;
                    Level topLevel = null;
                    double topOffset = -1;  // ft
                    double baseOffset = -1; // ft
                    baseLevel = doc.FindNearestLevel(data.BaseLevel / 304.8);
                    baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;
                    topLevel = doc.FindNearestLevel((data.BaseLevel + data.BaseOffset + data.Height) / 304.8);
                    topOffset = (data.BaseLevel + data.BaseOffset + data.Height) / 304.8 - topLevel.Elevation;
                    if (baseLevel == null)
                        continue;

                    // Step 2: Get family type
                    FamilySymbol symbol = null;
                    WallType wallType = null;
                    DuctType ductType = null;

                    if (data.TypeId != -1 && data.TypeId != 0)
                    {
                        ElementId typeELeId = ElementIdFactory.Create(data.TypeId);
                        if (typeELeId != null)
                        {
                            Element typeEle = doc.GetElement(typeELeId);
                            if (typeEle != null && typeEle is FamilySymbol)
                            {
                                symbol = typeEle as FamilySymbol;
                                // Get the symbol's Category and cast to BuiltInCategory enum
                                builtInCategory = (BuiltInCategory)symbol.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is WallType)
                            {
                                wallType = typeEle as WallType;
                                builtInCategory = (BuiltInCategory)wallType.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is DuctType)
                            {
                                ductType = typeEle as DuctType;
                                builtInCategory = (BuiltInCategory)ductType.Category.Id.GetIntValue();
                            }
                        }
                    }
                    if (builtInCategory == BuiltInCategory.INVALID)
                        continue;
                    switch (builtInCategory)
                    {
                        case BuiltInCategory.OST_Walls:
                            if (wallType == null)
                            {
                                // Requested typeId was invalid or not provided, fall back to first available
                                using (var wallTypeCollector = new FilteredElementCollector(doc))
                                {
                                    wallType = wallTypeCollector
                                        .OfClass(typeof(WallType))
                                        .Cast<WallType>()
                                        .FirstOrDefault();
                                }
                                if (wallType == null)
                                {
                                    _warnings.Add($"No wall types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested wall typeId {requestedTypeId} not found. Defaulted to '{wallType.Name}' (ID: {wallType.Id.GetValue()})");
                                }
                            }
                            break;
                        case BuiltInCategory.OST_DuctCurves:
                            if (ductType == null)
                            {
                                // Requested typeId was invalid or not provided, fall back to first available rectangular duct
                                using (var ductTypeCollector = new FilteredElementCollector(doc))
                                {
                                    ductType = ductTypeCollector
                                        .OfClass(typeof(DuctType))
                                        .Cast<DuctType>()
                                        .FirstOrDefault(d => d.Shape == ConnectorProfileType.Rectangular);
                                }
                                if (ductType == null)
                                {
                                    _warnings.Add($"No rectangular duct types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested duct typeId {requestedTypeId} not found. Defaulted to '{ductType.Name}' (ID: {ductType.Id.GetValue()})");
                                }
                            }
                            break;
                        default:
                            if (symbol == null)
                            {
                                using (var symCollector = new FilteredElementCollector(doc))
                                {
                                    symbol = symCollector
                                        .OfClass(typeof(FamilySymbol))
                                        .OfCategory(builtInCategory)
                                        .Cast<FamilySymbol>()
                                        .FirstOrDefault(fs => fs.IsActive); // Use the first active type as the default
                                }
                                if (symbol == null)
                                {
                                    using (var symCollector2 = new FilteredElementCollector(doc))
                                    {
                                        symbol = symCollector2
                                            .OfClass(typeof(FamilySymbol))
                                            .OfCategory(builtInCategory)
                                            .Cast<FamilySymbol>()
                                            .FirstOrDefault();
                                    }
                                }
                                if (symbol == null)
                                {
                                    _warnings.Add($"No family types available for category {builtInCategory}.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested typeId {requestedTypeId} not found. Defaulted to '{symbol.FamilyName}: {symbol.Name}' (ID: {symbol.Id.GetValue()})");
                                }
                            }
                            break;
                    }

                    // Step 3: Create element instances using the generic creation method
                    using (Transaction transaction = new Transaction(doc, "Create Point-Based Element"))
                    {
                        transaction.Start();
                        switch (builtInCategory)
                        {
                            case BuiltInCategory.OST_Walls:
                                // Apply requested thickness if specified and different from the resolved type
                                if (data.Thickness > 0)
                                {
                                    double requestedThicknessFt = data.Thickness / 304.8;
                                    double actualThicknessFt = wallType.Width;
                                    double toleranceFt = 1.0 / 304.8; // 1 mm tolerance

                                    if (Math.Abs(actualThicknessFt - requestedThicknessFt) > toleranceFt)
                                    {
                                        WallType thicknessMatchedType = null;
                                        try
                                        {
                                            thicknessMatchedType = CreateOrGetWallType(doc, requestedThicknessFt);
                                        }
                                        catch (Exception typeEx)
                                        {
                                            _warnings.Add($"Thickness {data.Thickness:F1}mm requested but could not create matching wall type: {typeEx.Message}. " +
                                                          $"Using '{wallType.Name}' ({actualThicknessFt * 304.8:F1}mm actual) instead.");
                                        }

                                        if (thicknessMatchedType != null)
                                        {
                                            wallType = thicknessMatchedType;
                                        }
                                        else if (thicknessMatchedType == null && Math.Abs(actualThicknessFt - requestedThicknessFt) > toleranceFt)
                                        {
                                            // Warning already added above if typeEx was thrown; ensure one exists if Duplicate returned null silently
                                            bool alreadyWarned = _warnings.Count > 0 &&
                                                _warnings[_warnings.Count - 1].Contains("could not create matching wall type");
                                            if (!alreadyWarned)
                                            {
                                                _warnings.Add($"Thickness {data.Thickness:F1}mm requested; wall type creation returned null. " +
                                                              $"Using '{wallType.Name}' ({actualThicknessFt * 304.8:F1}mm actual) instead.");
                                            }
                                        }
                                    }

                                    // Always report actual vs requested so the caller is never in the dark
                                    double finalThicknessMm = wallType.Width * 304.8;
                                    _warnings.Add($"Wall thickness: requested {data.Thickness:F1}mm, actual {finalThicknessMm:F1}mm (type '{wallType.Name}').");
                                }
                                Wall wall = null;
                                wall = Wall.Create
                                (
                                  doc,
                                  JZLine.ToLine(data.LocationLine),
                                  wallType.Id,
                                  baseLevel.Id,
                                  data.Height / 304.8,
                                  baseOffset,
                                  false,
                                  false
                                );
                                if (wall != null)
                                {
                                    elementIds.Add(wall.Id.GetIntValue());
                                }
                                break;
                            case BuiltInCategory.OST_DuctCurves:
                                Duct duct = null;
                                // Get MEP system type (required)
                                MEPSystemType mepSystemType;
                                using (var mepCollector = new FilteredElementCollector(doc))
                                {
                                    mepSystemType = mepCollector
                                        .OfClass(typeof(MEPSystemType))
                                        .Cast<MEPSystemType>()
                                        .FirstOrDefault(m => m.SystemClassification == MEPSystemClassification.SupplyAir);
                                }

                                if (mepSystemType != null)
                                {
                                    duct = Duct.Create(
                                        doc,
                                        mepSystemType.Id,
                                        ductType.Id,
                                        baseLevel.Id,
                                        JZLine.ToLine(data.LocationLine).GetEndPoint(0),
                                        JZLine.ToLine(data.LocationLine).GetEndPoint(1)
                                    );

                                    if (duct != null)
                                    {
                                        // Set height offset
                                        Parameter offsetParam = duct.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
                                        if (offsetParam != null)
                                            offsetParam.Set(baseOffset);
                                        elementIds.Add(duct.Id.GetIntValue());
                                    }
                                }
                                break;
                            default:
                                if (!symbol.IsActive)
                                    symbol.Activate();

                                // Create family instance using the generic creation method
                                var instance = doc.CreateInstance(symbol, null, JZLine.ToLine(data.LocationLine), baseLevel, topLevel, baseOffset, topOffset);
                                if (instance != null)
                                {
                                    elementIds.Add(instance.Id.GetIntValue());
                                }
                                break;
                        }
                        //doc.Refresh();
                        transaction.Commit();
                    }
                }
                // Every element in the batch can hit a `continue` - unknown category, no
                // wall type, no duct type, no symbol - leaving elementIds empty. Reporting
                // that as success told the caller the work was done.
                bool created = elementIds.Count > 0;
                string message = created
                    ? $"Created {elementIds.Count} element(s)."
                    : "No elements were created.";
                if (_warnings.Count > 0)
                {
                    message += "\n\nWarnings:\n  - " + string.Join("\n  - ", _warnings);
                }
                else if (!created)
                {
                    message += " No reason was recorded, which is itself a defect worth reporting.";
                }
                Result = new AIResult<List<int>>
                {
                    Success = created,
                    Message = message,
                    Response = elementIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating line-based element: {ex.Message}",
                };
                // (dialog removed: a modal TaskDialog here blocks the shared ExternalEvent
                //  queue for every other command. The message already reaches the caller
                //  through the result set just below/above.)
            }
            finally
            {
                _resetEvent.Set(); // Signal the waiting thread that the operation is complete
            }
        }

        /// <summary>
        /// Wait for creation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds.</param>
        /// <returns>True if the operation completed before the timeout; otherwise, false.</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Create Line-Based Element";
        }

        /// <summary>
        /// Creates or retrieves a wall type with the specified thickness.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="width">Width in feet.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private WallType CreateOrGetWallType(Document doc, double width = 200 / 304.8)
        {
            // If no valid type exists,
            // check for an existing wall type with the specified thickness first
            WallType existingType;
            using (var existCollector = new FilteredElementCollector(doc))
            {
                existingType = existCollector
                                    .OfClass(typeof(WallType))
                                    .Cast<WallType>()
                                    .FirstOrDefault(w => w.Name == $"{_wallName}{width * 304.8}mm");
            }
            if (existingType != null)
                return existingType;

            // Not found — create a new wall type based on an existing generic wall
            WallType baseWallType;
            using (var baseCollector = new FilteredElementCollector(doc))
            {
                baseWallType = baseCollector
                                    .OfClass(typeof(WallType))
                                    .Cast<WallType>()
                                    .FirstOrDefault(w => w.Name.Contains("Generic"));
            }
            if (baseWallType == null)
            {
                using (var fallbackCollector = new FilteredElementCollector(doc))
                {
                    baseWallType = fallbackCollector
                                        .OfClass(typeof(WallType))
                                        .Cast<WallType>()
                                        .FirstOrDefault();
                }
            }

            if (baseWallType == null)
                throw new InvalidOperationException("No usable base wall type found.");

            // Duplicate the wall type
            WallType newWallType = null;
            newWallType = baseWallType.Duplicate($"{_wallName}{width * 304.8}mm") as WallType;

            // Set wall thickness
            CompoundStructure cs = newWallType.GetCompoundStructure();
            if (cs != null)
            {
                // Get the material ID of the original layer
                ElementId materialId = cs.GetLayers().First().MaterialId;

                // Create a new single-layer compound structure
                CompoundStructureLayer newLayer = new CompoundStructureLayer(
                    width,  // Width (in feet)
                    MaterialFunctionAssignment.Structure,  // Function assignment
                    materialId  // Material ID
                );

                // Create new compound structure
                IList<CompoundStructureLayer> newLayers = new List<CompoundStructureLayer> { newLayer };
                cs.SetLayers(newLayers);

                // Apply the new compound structure
                newWallType.SetCompoundStructure(cs);
            }
            return newWallType;
        }

        /// <summary>
        /// Creates or retrieves a duct type with the specified dimensions.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="width">Width in feet.</param>
        /// <param name="height">Height in feet.</param>
        /// <returns>The duct type.</returns>
        private DuctType CreateOrGetDuctType(Document doc, double width, double height)
        {
            string typeName = $"{_ductName}{width * 304.8}x{height * 304.8}mm";

            // Check for an existing duct type with the specified dimensions first
            DuctType existingType;
            using (var existCollector = new FilteredElementCollector(doc))
            {
                existingType = existCollector
                                    .OfClass(typeof(DuctType))
                                    .Cast<DuctType>()
                                    .FirstOrDefault(d => d.Name == typeName && d.Shape == ConnectorProfileType.Rectangular);
            }

            if (existingType != null)
                return existingType;

            // Not found — create a new duct type based on an existing rectangular duct type
            DuctType baseDuctType;
            using (var baseCollector = new FilteredElementCollector(doc))
            {
                baseDuctType = baseCollector
                                    .OfClass(typeof(DuctType))
                                    .Cast<DuctType>()
                                    .FirstOrDefault(d => d.Shape == ConnectorProfileType.Rectangular);
            }

            if (baseDuctType == null)
                throw new InvalidOperationException("No usable base rectangular duct type found.");

            // Duplicate the duct type
            DuctType newDuctType = baseDuctType.Duplicate(typeName) as DuctType;

            // Set duct dimension parameters
            Parameter widthParam = newDuctType.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            Parameter heightParam = newDuctType.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

            if (widthParam != null && heightParam != null)
            {
                widthParam.Set(width);
                heightParam.Set(height);
            }

            return newDuctType;
        }

    }
}
