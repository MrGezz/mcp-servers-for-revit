using RevitMCPCommandSet.Utils;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Architecture
{
    public class CreateRoofEventHandler : WaitableEventHandlerBase, IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;


        public List<RoofInfo> RoofData { get; private set; }

        public AIResult<List<int>> Result { get; private set; }

        private List<string> _warnings = new List<string>();

        public void SetParameters(List<RoofInfo> data)
        {
            RoofData = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();

                foreach (var info in RoofData)
                {
                    Level level = FindNearestLevel(info.Level / 304.8);
                    if (level == null) continue;

                    RoofType roofType = null;
                    if (info.Options != null && info.Options.TryGetValue("typeId", out object typeIdObj) && typeIdObj is long typeIdLong)
                    {
                        roofType = _doc.GetElement(ElementIdFactory.Create((int)typeIdLong)) as RoofType;
                    }

                    if (roofType == null && !string.IsNullOrEmpty(info.Type))
                    {
                        using (var fec = new FilteredElementCollector(_doc))
                        {
                            roofType = fec
                                .OfClass(typeof(RoofType))
                                .Cast<RoofType>()
                                .FirstOrDefault(rt => rt.Name.Equals(info.Type, StringComparison.OrdinalIgnoreCase));
                        }
                        if (roofType == null)
                        {
                            _warnings.Add($"Roof type '{info.Type}' not found, using first available");
                        }
                    }

                    if (roofType == null)
                    {
                        using (var fec = new FilteredElementCollector(_doc))
                        {
                            roofType = fec
                                .OfClass(typeof(RoofType))
                                .Cast<RoofType>()
                                .FirstOrDefault();
                        }
                    }

                    if (roofType == null) continue;

                    using (Transaction tx = new Transaction(_doc, "Create Roof"))
                    {
                        tx.Start();

                        try
                        {
                            double elevationInFeet = info.Level / 304.8;
                            double widthInFeet = (info.Options != null && info.Options.TryGetValue("width", out object w)) ? Convert.ToDouble(w) / 304.8 : 30.0 / 304.8;
                            double lengthInFeet = (info.Options != null && info.Options.TryGetValue("length", out object l)) ? Convert.ToDouble(l) / 304.8 : 30.0 / 304.8;

                            // Determine roof shape: "extrusion" or "footprint" (default)
                            string shape = "footprint";
                            if (info.Options != null && info.Options.TryGetValue("shape", out object shapeObj))
                            {
                                shape = shapeObj?.ToString()?.ToLower() ?? "footprint";
                            }

                            if (shape == "extrusion")
                            {
                                // Extrusion roof requires a reference plane
                                int referencePlaneId = 0;
                                if (info.Options != null && info.Options.TryGetValue("referencePlaneId", out object rpIdObj))
                                {
                                    referencePlaneId = Convert.ToInt32(rpIdObj);
                                }

                                if (referencePlaneId <= 0)
                                {
                                    _warnings.Add("Extrusion roof requires a 'referencePlaneId' in options; skipping");
                                    tx.RollBack();
                                    continue;
                                }

                                ReferencePlane refPlane = _doc.GetElement(ElementIdFactory.Create(referencePlaneId)) as ReferencePlane;
                                if (refPlane == null)
                                {
                                    _warnings.Add($"Reference plane with ID {referencePlaneId} not found; skipping");
                                    tx.RollBack();
                                    continue;
                                }

                                double extrusionStart = (info.Options != null && info.Options.TryGetValue("extrusionStart", out object esObj)) ? Convert.ToDouble(esObj) / 304.8 : 0.0;
                                double extrusionEnd = (info.Options != null && info.Options.TryGetValue("extrusionEnd", out object eeObj)) ? Convert.ToDouble(eeObj) / 304.8 : lengthInFeet;

                                // The profile MUST lie ON the reference plane. Building it
                                // in global XYZ with Y=0 - as this first did - only
                                // satisfies that when the caller's plane happens to span
                                // the global XZ plane. For any other plane the curves are
                                // not coplanar with it, NewExtrusionRoof throws
                                // ArgumentException, the outer catch swallows it, and the
                                // caller gets no roof and no reason.
                                //
                                // So the profile is built in the PLANE'S OWN BASIS:
                                // GetPlane() is identical across Revit 2022-2027, and its
                                // XVec/YVec span the plane, so every point below is on it
                                // by construction rather than by coincidence.
                                Plane sketchPlane = refPlane.GetPlane();
                                XYZ planeOrigin = sketchPlane.Origin;
                                XYZ alongWidth = sketchPlane.XVec;
                                XYZ upInPlane = sketchPlane.YVec;

                                // (width across the plane, rise within the plane) -> model point
                                Func<double, double, XYZ> onPlane = (w, rise) =>
                                    planeOrigin + (alongWidth * w) + (upInPlane * (elevationInFeet + rise));

                                CurveArray profile = new CurveArray();
                                double heightInFeet = info.Height / 304.8;
                                if (heightInFeet > 0)
                                {
                                    // Gable profile
                                    profile.Append(Line.CreateBound(
                                        onPlane(0, 0),
                                        onPlane(widthInFeet / 2, heightInFeet)));
                                    profile.Append(Line.CreateBound(
                                        onPlane(widthInFeet / 2, heightInFeet),
                                        onPlane(widthInFeet, 0)));
                                }
                                else
                                {
                                    // Flat profile
                                    profile.Append(Line.CreateBound(
                                        onPlane(0, 0),
                                        onPlane(widthInFeet, 0)));
                                }

                                ExtrusionRoof extRoof = _doc.Create.NewExtrusionRoof(profile, refPlane, level, roofType, extrusionStart, extrusionEnd);

                                if (extRoof != null)
                                {
                                    elementIds.Add(extRoof.Id.GetIntValue());
                                }
                            }
                            else
                            {
                                // Footprint roof (default path)
                                CurveArray curveArray = new CurveArray();
                                var p1 = new XYZ(0, 0, elevationInFeet);
                                var p2 = new XYZ(widthInFeet, 0, elevationInFeet);
                                var p3 = new XYZ(widthInFeet, lengthInFeet, elevationInFeet);
                                var p4 = new XYZ(0, lengthInFeet, elevationInFeet);

                                curveArray.Append(Line.CreateBound(p1, p2));
                                curveArray.Append(Line.CreateBound(p2, p3));
                                curveArray.Append(Line.CreateBound(p3, p4));
                                curveArray.Append(Line.CreateBound(p4, p1));

                                ModelCurveArray modelCurveArray = new ModelCurveArray();
                                FootPrintRoof roof = _doc.Create.NewFootPrintRoof(curveArray, level, roofType, out modelCurveArray);

                                if (roof != null)
                                {
                                    if (info.Slope > 0)
                                    {
                                        double slopeValue = Math.Tan(info.Slope * Math.PI / 180.0);
                                        foreach (ModelCurve mc in modelCurveArray)
                                        {
                                            roof.set_DefinesSlope(mc, true);
                                            roof.set_SlopeAngle(mc, slopeValue);
                                        }
                                    }

                                    elementIds.Add(roof.Id.GetIntValue());
                                }
                            }

                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            _warnings.Add($"Failed to create roof: {ex.Message}");
                        }
                    }
                }

                string message = $"Successfully created {elementIds.Count} roof(s)";
                if (_warnings.Count > 0)
                {
                    message += "\nWarnings:\n  " + string.Join("\n  ", _warnings);
                }

                Result = new AIResult<List<int>>
                {
                    Success = true,
                    Message = message,
                    Response = elementIds
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating roofs: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private Level FindNearestLevel(double elevationInFeet)
        {
            List<Level> levels;
            using (var fec = new FilteredElementCollector(_doc))
            {
                levels = fec
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();
            }

            Level nearestLevel = null;
            double minDistance = double.MaxValue;

            foreach (var level in levels)
            {
                double distance = Math.Abs(level.Elevation - elevationInFeet);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestLevel = level;
                }
            }

            return nearestLevel;
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Create Roof";
        }
    }
}
