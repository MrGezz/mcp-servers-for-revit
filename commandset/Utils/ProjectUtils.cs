using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands;
using RevitMCPCommandSet.Models.Common;
using System.IO;
using System.Reflection;

namespace RevitMCPCommandSet.Utils
{
    public static class ProjectUtils
    {
        /// <summary>
        /// General method for creating a family instance
        /// </summary>
        /// <param name="doc">Current document</param>
        /// <param name="familySymbol">Family symbol type</param>
        /// <param name="locationPoint">Placement point</param>
        /// <param name="locationLine">Base line</param>
        /// <param name="baseLevel">Base level</param>
        /// <param name="topLevel">Second level (used for TwoLevelsBased)</param>
        /// <param name="baseOffset">Base offset (ft)</param>
        /// <param name="topOffset">Top offset (ft)</param>
        /// <param name="faceDirection">Reference direction</param>
        /// <param name="handDirection">Reference direction</param>
        /// <param name="view">View</param>
        /// <returns>The created family instance, or null on failure</returns>
        public static FamilyInstance CreateInstance(
            this Document doc,
            FamilySymbol familySymbol,
            XYZ locationPoint = null,
            Line locationLine = null,
            Level baseLevel = null,
            Level topLevel = null,
            double baseOffset = -1,
            double topOffset = -1,
            XYZ faceDirection = null,
            XYZ handDirection = null,
            View view = null,
            Element explicitHost = null,
            bool snapToHostCenter = true)
        {
            // Basic parameter validation
            if (doc == null)
                throw new ArgumentNullException($"Required parameter {typeof(Document)} {nameof(doc)} is missing!");
            if (familySymbol == null)
                throw new ArgumentNullException($"Required parameter {typeof(FamilySymbol)} {nameof(familySymbol)} is missing!");

            // Activate the family symbol
            if (!familySymbol.IsActive)
                familySymbol.Activate();

            FamilyInstance instance = null;

            // Select the creation method based on the family placement type
            switch (familySymbol.Family.FamilyPlacementType)
            {
                // Families based on a single level (e.g., Generic Model)
                case FamilyPlacementType.OneLevelBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing!");
                    // With level information
                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // Physical location where the instance will be placed
                            familySymbol,                   // FamilySymbol object representing the type of instance to insert
                            baseLevel,                      // Level object used as the base level for the object
                            StructuralType.NonStructural);  // Specifies the structural type if the element is structural
                    }
                    // Without level information
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // Physical location where the instance will be placed
                            familySymbol,                   // FamilySymbol object representing the type of instance to insert
                            StructuralType.NonStructural);  // Specifies the structural type if the element is structural
                    }
                    break;

                // Families based on a single level and a host (e.g., doors, windows)
                case FamilyPlacementType.OneLevelBasedHosted:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing!");

                    Element host = explicitHost;
                    XYZ placementPoint = locationPoint;

                    // If explicit host provided and it's a wall, snap to its centerline
                    if (host != null && snapToHostCenter && host is Wall explicitWall)
                    {
                        LocationCurve eLoc = explicitWall.Location as LocationCurve;
                        if (eLoc != null)
                        {
                            IntersectionResult eIr = eLoc.Curve.Project(locationPoint);
                            if (eIr != null)
                                placementPoint = new XYZ(eIr.XYZPoint.X, eIr.XYZPoint.Y, locationPoint.Z);
                        }
                    }

                    // Auto-detect host wall if not explicitly provided
                    if (host == null)
                    {
                        // Try geometric wall-centerline proximity first
                        var wallResult = doc.GetNearestWallByLocationLine(locationPoint, baseLevel);
                        if (wallResult.HasValue)
                        {
                            host = wallResult.Value.wall;
                            if (snapToHostCenter)
                                placementPoint = wallResult.Value.projectedPoint;
                        }
                        else
                        {
                            // Fall back to original ray-casting method
                            host = doc.GetNearestHostElement(locationPoint, familySymbol);
                        }
                    }

                    if (host == null)
                        throw new ArgumentNullException($"No valid host element could be found!");

                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            baseLevel,
                            StructuralType.NonStructural);
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            StructuralType.NonStructural);
                    }

                    // Set sill height for windows (baseOffset maps to sill height for hosted elements)
                    if (instance != null && baseOffset != -1)
                    {
                        Parameter sillParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                        if (sillParam != null && !sillParam.IsReadOnly)
                        {
                            sillParam.Set(baseOffset);
                        }
                    }
                    break;

                // Families based on two levels (e.g., columns)
                case FamilyPlacementType.TwoLevelsBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing!");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Level)} {nameof(baseLevel)} is missing!");
                    // Determine whether this is a structural or architectural column
                    StructuralType structuralType = StructuralType.NonStructural;
                    if (familySymbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralColumns)
                        structuralType = StructuralType.Column;
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,              // Physical location where the instance will be placed
                        familySymbol,               // FamilySymbol object representing the type of instance to insert
                        baseLevel,                  // Level object used as the base level for the object
                        structuralType);            // Specifies the structural type if the element is structural
                    // Set base level, top level, base offset, and top offset
                    if (instance != null)
                    {
                        // Set the column's base level and top level
                        if (baseLevel != null)
                        {
                            Parameter baseLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                            if (baseLevelParam != null)
                                baseLevelParam.Set(baseLevel.Id);
                        }
                        if (topLevel != null)
                        {
                            Parameter topLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topLevelParam != null)
                                topLevelParam.Set(topLevel.Id);
                        }
                        // Get the base offset parameter
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert from millimetres to Revit internal units
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                        // Get the top offset parameter
                        if (topOffset != -1)
                        {
                            Parameter topOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                            if (topOffsetParam != null && topOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert from millimetres to Revit internal units
                                double topOffsetInternal = topOffset;
                                topOffsetParam.Set(topOffsetInternal);
                            }
                        }
                    }
                    break;

                // Families that are view-specific (e.g., detail annotations)
                case FamilyPlacementType.ViewBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing!");
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,  // Origin of the family instance. If placed in a plan view (ViewPlan), the origin is projected onto the view plane
                        familySymbol,   // Family symbol object representing the type of instance to insert
                        view);          // 2D view in which to place the family instance
                    break;

                // Families based on a work plane (e.g., face-based Generic Model, including face-based and wall-based)
                case FamilyPlacementType.WorkPlaneBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Required parameter {typeof(XYZ)} {nameof(locationPoint)} is missing!");
                    // Find the nearest host face
                    Reference hostFace = doc.GetNearestFaceReference(locationPoint, 1000 / 304.8);
                    if (hostFace == null)
                        throw new ArgumentNullException($"No valid host element could be found!");
                    if (faceDirection == null || faceDirection == XYZ.Zero)
                    {
                        var result = doc.GenerateDefaultOrientation(hostFace);
                        faceDirection = result.FacingOrientation;
                    }
                    // Create a family instance on the face using a point and orientation
                    instance = doc.Create.NewFamilyInstance(
                        hostFace,               // Reference to the face  
                        locationPoint,          // Point on the face where the instance will be placed
                        faceDirection,          // Vector defining the orientation of the family instance. Note: this direction defines the instance's rotation on the face and must not be parallel to the face normal
                        familySymbol);          // FamilySymbol object representing the type of instance to insert. Note: this FamilySymbol must represent a family with FamilyPlacementType WorkPlaneBased
                    break;

                // Line-based families on a work plane (e.g., line-based Generic Model)
                case FamilyPlacementType.CurveBased:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing!");

                    // Find the nearest host face (zero-tolerance)
                    Reference lineHostFace = doc.GetNearestFaceReference(locationLine.Evaluate(0.5, true), 1e-5);
                    if (lineHostFace != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            lineHostFace,   // Reference to the face 
                            locationLine,   // Curve on which the family instance is based
                            familySymbol);  // A FamilySymbol representing the type of instance to insert. Note: this symbol must represent a family whose FamilyPlacementType is WorkPlaneBased or CurveBased
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationLine,                   // Curve on which the family instance is based
                            familySymbol,                   // A FamilySymbol representing the type of instance to insert. Note: this symbol must represent a family whose FamilyPlacementType is WorkPlaneBased or CurveBased
                            baseLevel,                      // A Level object used as the base level for the object
                            StructuralType.NonStructural);  // Specifies the structural type if the element is structural
                    }
                    if (instance != null)
                    {
                        // Get the base offset parameter
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Convert from millimetres to Revit internal units
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                    }
                    break;

                // Line-based families in a specific view (e.g., detail components)
                case FamilyPlacementType.CurveBasedDetail:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing!");
                    if (view == null)
                        throw new ArgumentNullException($"Required parameter {typeof(View)} {nameof(view)} is missing!");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,   // Line position of the family instance. The line must lie within the view plane
                        familySymbol,   // Family symbol object representing the type of instance to insert
                        view);          // 2D view in which to place the family instance
                    break;

                // Curve-driven structural families (e.g., beams, braces, or slanted columns)
                case FamilyPlacementType.CurveDrivenStructural:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Line)} {nameof(locationLine)} is missing!");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Required parameter {typeof(Level)} {nameof(baseLevel)} is missing!");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,                   // Curve on which the family instance is based
                        familySymbol,                   // A FamilySymbol representing the type of instance to insert. Note: this symbol must represent a family whose FamilyPlacementType is WorkPlaneBased or CurveBased
                        baseLevel,                      // A Level object used as the base level for the object
                        StructuralType.Beam);           // Specifies the structural type if the element is structural
                    break;

                // Adaptive families (e.g., Adaptive Generic Model, curtain wall panels)
                case FamilyPlacementType.Adaptive:
                    throw new NotImplementedException("FamilyPlacementType.Adaptive placement is not implemented!");

                default:
                    break;
            }
            return instance;
        }

        /// <summary>
        /// Computes the default facing and hand orientations (the longer axis maps to HandOrientation, the shorter to FacingOrientation)
        /// </summary>
        /// <param name="hostFace"></param>
        /// <returns></returns>
        public static (XYZ FacingOrientation, XYZ HandOrientation) GenerateDefaultOrientation(this Document doc, Reference hostFace)
        {
            var facingOrientation = new XYZ();  // Facing direction: the direction the family's positive Y-axis points to after loading
            var handOrientation = new XYZ();    // Hand direction: the direction the family's positive X-axis points to after loading

            // Step 1: Retrieve the face object from the Reference
            Face face = doc.GetElement(hostFace.ElementId).GetGeometryObjectFromReference(hostFace) as Face;

            // Step 2: Retrieve the face outline
            List<Curve> profile = null;
            // Collection of outline loops; each sub-list is a complete closed loop; the first is typically the outer outline
            List<List<Curve>> profiles = new List<List<Curve>>();
            // Retrieve all edge loops (outer outline and any inner holes)
            EdgeArrayArray edgeLoops = face.EdgeLoops;
            // Iterate through each edge loop
            foreach (EdgeArray loop in edgeLoops)
            {
                List<Curve> currentLoop = new List<Curve>();
                // Retrieve each edge in the loop
                foreach (Edge edge in loop)
                {
                    Curve curve = edge.AsCurve();
                    currentLoop.Add(curve);
                }
                // If the current loop has edges, add it to the results
                if (currentLoop.Count > 0)
                {
                    profiles.Add(currentLoop);
                }
            }
            // The first loop is typically the outer outline
            if (profiles != null && profiles.Any())
                profile = profiles.FirstOrDefault();

            // Step 3: Retrieve the face normal
            XYZ faceNormal = null;
            // If the face is planar, the normal can be read directly from the property
            if (face is PlanarFace planarFace)
                faceNormal = planarFace.FaceNormal;

            // Step 4: Compute the two principal directions of the face (right-hand rule compliant)
            var result = face.GetMainDirections();
            var primaryDirection = result.PrimaryDirection;
            var secondaryDirection = result.SecondaryDirection;

            // By default the longer-edge direction is HandOrientation and the shorter-edge direction is FacingOrientation
            facingOrientation = primaryDirection;
            handOrientation = secondaryDirection;

            // Check right-hand rule compliance (thumb: HandOrientation, index: FacingOrientation, middle: FaceNormal)
            if (!facingOrientation.IsRightHandRuleCompliant(handOrientation, faceNormal))
            {
                var newHandOrientation = facingOrientation.GenerateIndexFinger(faceNormal);
                if (newHandOrientation != null)
                {
                    handOrientation = newHandOrientation;
                }
            }

            return (facingOrientation, handOrientation);
        }

        /// <summary>
        /// Returns the face Reference closest to the given point
        /// </summary>
        /// <param name="doc">Current document</param>
        /// <param name="location">Target point position</param>
        /// <param name="radius">Search radius (internal units)</param>
        /// <returns>Reference to the nearest face, or null if none is found</returns>
        public static Reference GetNearestFaceReference(this Document doc, XYZ location, double radius = 1000 / 304.8)
        {
            try
            {
                // Offset slightly to avoid numerical precision issues
                location = new XYZ(location.X, location.Y, location.Z + 0.1 / 304.8);

                // Find or create a 3D view
                View3D view3D = null;
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));

                foreach (View3D v in collector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    Diagnostics.Report("Error", "Unable to create or retrieve a 3D view");
                    return null;
                }

                // Set up rays in 6 directions
                XYZ[] directions = new XYZ[]
                {
                  XYZ.BasisX,    // Positive X direction
                  -XYZ.BasisX,   // Negative X direction
                  XYZ.BasisY,    // Positive Y direction
                  -XYZ.BasisY,   // Negative Y direction
                  XYZ.BasisZ,    // Positive Z direction
                  -XYZ.BasisZ    // Negative Z direction
                };

                // Build element filters
                ElementClassFilter wallFilter = new ElementClassFilter(typeof(Wall));
                ElementClassFilter floorFilter = new ElementClassFilter(typeof(Floor));
                ElementClassFilter ceilingFilter = new ElementClassFilter(typeof(Ceiling));
                ElementClassFilter instanceFilter = new ElementClassFilter(typeof(FamilyInstance));

                // Combine filters with logical OR
                LogicalOrFilter categoryFilter = new LogicalOrFilter(
                    new ElementFilter[] { wallFilter, floorFilter, ceilingFilter, instanceFilter });


                // 1. Simplest option: filter for all instantiated elements
                //ElementFilter filter = new ElementIsElementTypeFilter(true);

                // Create the reference intersector
                ReferenceIntersector refIntersector = new ReferenceIntersector(categoryFilter,
                    FindReferenceTarget.Face, view3D);
                refIntersector.FindReferencesInRevitLinks = true; // Search linked files as well

                double minDistance = double.MaxValue;
                Reference nearestFace = null;

                foreach (XYZ direction in directions)
                {
                    // Cast a ray from the current position
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity; // Distance to the face

                        // If within the search radius and closer than the current best
                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestFace = rwc.GetReference();
                        }
                    }
                }

                return nearestFace;
            }
            catch (Exception ex)
            {
                Diagnostics.Report("Error", $"An error occurred while finding the nearest face: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the host element closest to the given point
        /// </summary>
        /// <param name="doc">Current document</param>
        /// <param name="location">Target point position</param>
        /// <param name="familySymbol">Family symbol, used to determine the expected host type</param>
        /// <param name="radius">Search radius (internal units)</param>
        /// <returns>The nearest host element, or null if none is found</returns>
        public static Element GetNearestHostElement(this Document doc, XYZ location, FamilySymbol familySymbol, double radius = 5.0)
        {
            try
            {
                // Basic parameter validation
                if (doc == null || location == null || familySymbol == null)
                    return null;

                // Retrieve the family's hosting behaviour parameter
                Parameter hostParam = familySymbol.Family.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR);
                int hostingBehavior = hostParam?.AsInteger() ?? 0;

                // Find or create a 3D view
                View3D view3D = null;
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));
                foreach (View3D v in viewCollector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    Diagnostics.Report("Error", "Unable to create or retrieve a 3D view");
                    return null;
                }

                // Build a type filter based on the hosting behaviour
                ElementFilter classFilter;
                switch (hostingBehavior)
                {
                    case 1: // Wall based
                        classFilter = new ElementClassFilter(typeof(Wall));
                        break;
                    case 2: // Floor based
                        classFilter = new ElementClassFilter(typeof(Floor));
                        break;
                    case 3: // Ceiling based
                        classFilter = new ElementClassFilter(typeof(Ceiling));
                        break;
                    case 4: // Roof based
                        classFilter = new ElementClassFilter(typeof(RoofBase));
                        break;
                    default:
                        return null; // Unsupported host type
                }

                // Set up rays in 6 directions
                XYZ[] directions = new XYZ[]
                {
                    XYZ.BasisX,    // Positive X direction
                    -XYZ.BasisX,   // Negative X direction
                    XYZ.BasisY,    // Positive Y direction
                    -XYZ.BasisY,   // Negative Y direction
                    XYZ.BasisZ,    // Positive Z direction
                    -XYZ.BasisZ    // Negative Z direction
                };

                // Create the reference intersector
                ReferenceIntersector refIntersector = new ReferenceIntersector(classFilter,
                    FindReferenceTarget.Element, view3D);
                refIntersector.FindReferencesInRevitLinks = true; // Search linked files for elements as well

                double minDistance = double.MaxValue;
                Element nearestHost = null;

                foreach (XYZ direction in directions)
                {
                    // Cast a ray from the current position
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity; // Distance to the element

                        // If within the search radius and closer than the current best
                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestHost = doc.GetElement(rwc.GetReference().ElementId);
                        }
                    }
                }

                return nearestHost;
            }
            catch (Exception ex)
            {
                Diagnostics.Report("Error", $"An error occurred while finding the nearest host element: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds the nearest wall to a point using wall location-line distance calculation.
        /// More reliable than ray-casting for door/window placement.
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="point">Target point (internal units, feet)</param>
        /// <param name="level">Level to filter walls on</param>
        /// <param name="tolerance">Extra tolerance beyond half wall width (feet). Default ~5mm.</param>
        /// <returns>Tuple of (wall, projectedPoint, wallDirection, distance) or null</returns>
        public static (Wall wall, XYZ projectedPoint, XYZ wallDirection, double distance)?
            GetNearestWallByLocationLine(
                this Document doc,
                XYZ point,
                Level level,
                double tolerance = 5.0 / 304.8)
        {
            if (doc == null || point == null || level == null)
                return null;

            // Collect all walls on the given level
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w =>
                {
                    Parameter baseLevelParam = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    return baseLevelParam != null && baseLevelParam.AsElementId() == level.Id;
                })
                .ToList();

            Wall bestWall = null;
            XYZ bestProjection = null;
            XYZ bestDirection = null;
            double bestDistance = double.MaxValue;

            foreach (Wall wall in walls)
            {
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve curve = locCurve.Curve;
                if (curve == null) continue;

                // Use Curve.Project() which handles both lines and arcs
                IntersectionResult ir = curve.Project(new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z));
                if (ir == null) continue;

                XYZ projectedPt = ir.XYZPoint;
                double distance = new XYZ(point.X - projectedPt.X, point.Y - projectedPt.Y, 0).GetLength();

                // Check if point is within half the wall width + tolerance
                double halfWidth = wall.Width / 2.0;
                if (distance <= halfWidth + tolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestWall = wall;
                    bestProjection = new XYZ(projectedPt.X, projectedPt.Y, point.Z);

                    // Compute wall direction from curve tangent at projected parameter
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);
                    bestDirection = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0).Normalize();
                }
            }

            if (bestWall == null)
                return null;

            return (bestWall, bestProjection, bestDirection, bestDistance);
        }

        /// <summary>
        /// Highlights the specified face in the active view
        /// </summary>
        /// <param name="doc">Current document</param>
        /// <param name="faceRef">Reference to the face to highlight</param>
        /// <param name="duration">Highlight duration in milliseconds (default 3000 ms)</param>
        public static void HighlightFace(this Document doc, Reference faceRef)
        {
            if (faceRef == null) return;

            // Find the solid fill pattern
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);

            if (solidFill == null)
            {
                Diagnostics.Report("Error", "Solid fill pattern not found");
                return;
            }

            // Build the graphic override settings
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(new Color(255, 0, 0)); // Red
            ogs.SetSurfaceForegroundPatternId(solidFill.Id);
            ogs.SetSurfaceTransparency(0); // Opaque

            // Apply the override
            doc.ActiveView.SetElementOverrides(faceRef.ElementId, ogs);
        }

        /// <summary>
        /// Extracts the two principal direction vectors of a face
        /// </summary>
        /// <param name="face">Input face</param>
        /// <returns>Tuple containing the primary and secondary directions</returns>
        /// <exception cref="ArgumentNullException">Thrown when the face is null</exception>
        /// <exception cref="ArgumentException">Thrown when the face outline is insufficient to form a valid shape</exception>
        /// <exception cref="InvalidOperationException">Thrown when valid directions cannot be extracted</exception>
        public static (XYZ PrimaryDirection, XYZ SecondaryDirection) GetMainDirections(this Face face)
        {
            // 1. Parameter validation
            if (face == null)
                throw new ArgumentNullException(nameof(face), "face cannot be null");

            // 2. Compute the face normal, needed if a perpendicular vector must be constructed later
            XYZ faceNormal = face.ComputeNormal(new UV(0.5, 0.5));

            // 3. Retrieve the outer outline of the face
            EdgeArrayArray edgeLoops = face.EdgeLoops;
            if (edgeLoops.Size == 0)
                throw new ArgumentException("The face has no valid edge loops", nameof(face));

            // The first loop is typically the outer outline
            EdgeArray outerLoop = edgeLoops.get_Item(0);

            // 4. Compute the direction vector and length of each edge
            List<XYZ> edgeDirections = new List<XYZ>();  // Unit direction vector for each edge
            List<double> edgeLengths = new List<double>(); // Length of each edge

            foreach (Edge edge in outerLoop)
            {
                Curve curve = edge.AsCurve();
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);

                // Compute the vector from start to end
                XYZ direction = endPoint - startPoint;
                double length = direction.GetLength();

                // Skip very short edges (likely due to coincident vertices or floating-point precision)
                if (length > 1e-10)
                {
                    edgeDirections.Add(direction.Normalize());  // Store the normalised direction vector
                    edgeLengths.Add(length);                    // Store the edge length
                }
            }

            if (edgeDirections.Count < 4) // Require at least 4 edges
            {
                throw new ArgumentException("The face does not have enough edges to form a valid shape", nameof(face));
            }

            // 5. Group edges with similar directions
            List<List<int>> directionGroups = new List<List<int>>();  // Direction groups; each group holds the indices of its member edges

            for (int i = 0; i < edgeDirections.Count; i++)
            {
                bool foundGroup = false;
                XYZ currentDirection = edgeDirections[i];

                // Try to assign the current edge to an existing direction group
                for (int j = 0; j < directionGroups.Count; j++)
                {
                    var group = directionGroups[j];
                    // Compute the weighted-average direction of the current group
                    XYZ groupAvgDir = CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths);

                    // Check whether the current direction is similar to the group's average (including the reverse direction)
                    double dotProduct = Math.Abs(groupAvgDir.DotProduct(currentDirection));
                    if (dotProduct > 0.8) // Deviation within ~30 degrees is treated as the same direction
                    {
                        group.Add(i);  // Add this edge's index to the group
                        foundGroup = true;
                        break;
                    }
                }

                // If the current edge matches no existing group, start a new one
                if (!foundGroup)
                {
                    List<int> newGroup = new List<int> { i };
                    directionGroups.Add(newGroup);
                }
            }

            // 6. Compute the total weight (sum of edge lengths) and average direction for each group
            List<double> groupWeights = new List<double>();
            List<XYZ> groupDirections = new List<XYZ>();

            foreach (var group in directionGroups)
            {
                // Sum the lengths of all edges in the group
                double totalLength = 0;
                foreach (int edgeIndex in group)
                {
                    totalLength += edgeLengths[edgeIndex];
                }
                groupWeights.Add(totalLength);

                // Compute the weighted-average direction for the group
                groupDirections.Add(CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths));
            }

            // 7. Sort by weight and extract the principal directions
            int[] sortedIndices = Enumerable.Range(0, groupDirections.Count)
                .OrderByDescending(i => groupWeights[i])
                .ToArray();

            // 8. Build the result
            if (groupDirections.Count >= 2)
            {
                // At least two direction groups: pick the two heaviest as primary and secondary
                int primaryIndex = sortedIndices[0];
                int secondaryIndex = sortedIndices[1];

                return (
                    PrimaryDirection: groupDirections[primaryIndex],      // Primary direction
                    SecondaryDirection: groupDirections[secondaryIndex]   // Secondary direction
                );
            }
            else if (groupDirections.Count == 1)
            {
                // Only one direction group: construct a secondary direction perpendicular to the primary
                XYZ primaryDirection = groupDirections[0];
                // Use the cross product of the face normal and the primary direction to build the perpendicular
                XYZ secondaryDirection = faceNormal.CrossProduct(primaryDirection).Normalize();

                return (
                    PrimaryDirection: primaryDirection,         // Primary direction 
                    SecondaryDirection: secondaryDirection      // Synthetically constructed perpendicular secondary direction
                );
            }
            else
            {
                // Unable to extract valid directions (rare)
                throw new InvalidOperationException("Unable to extract valid directions from the face");
            }
        }

        /// <summary>
        /// Computes the edge-length-weighted average direction for a group of edges
        /// </summary>
        /// <param name="edgeIndices">List of edge indices in the group</param>
        /// <param name="directions">Direction vectors for all edges</param>
        /// <param name="lengths">Lengths of all edges</param>
        /// <returns>Normalised weighted-average direction vector</returns>
        public static XYZ CalculateWeightedAverageDirection(List<int> edgeIndices, List<XYZ> directions, List<double> lengths)
        {
            if (edgeIndices.Count == 0)
                return null;

            double sumX = 0, sumY = 0, sumZ = 0;
            XYZ referenceDir = directions[edgeIndices[0]];  // Use the first edge in the group as the reference direction

            foreach (int i in edgeIndices)
            {
                XYZ currentDir = directions[i];

                // Compute the dot product with the reference direction to decide whether to flip
                double dot = referenceDir.DotProduct(currentDir);

                // If the direction is opposite (negative dot product), negate it before accumulating
                // This keeps all vectors in the group pointing the same way, preventing cancellation
                double factor = (dot >= 0) ? lengths[i] : -lengths[i];

                // Accumulate weighted vector components
                sumX += currentDir.X * factor;
                sumY += currentDir.Y * factor;
                sumZ += currentDir.Z * factor;
            }

            // Build the composite vector and normalise
            XYZ avgDir = new XYZ(sumX, sumY, sumZ);
            double magnitude = avgDir.GetLength();

            // Guard against a zero-length vector
            if (magnitude < 1e-10)
                return referenceDir;  // Fall back to the reference direction

            return avgDir.Normalize();  // Return the normalised direction vector
        }

        /// <summary>
        /// Checks whether three vectors satisfy the right-hand rule and are mutually orthogonal
        /// </summary>
        /// <param name="thumb">Thumb direction vector</param>
        /// <param name="indexFinger">Index-finger direction vector</param>
        /// <param name="middleFinger">Middle-finger direction vector</param>
        /// <param name="tolerance">Comparison tolerance (default 1e-6)</param>
        /// <returns>True if the three vectors are mutually orthogonal and right-hand rule compliant; otherwise false</returns>
        public static bool IsRightHandRuleCompliant(this XYZ thumb, XYZ indexFinger, XYZ middleFinger, double tolerance = 1e-6)
        {
            // Check that all three vectors are mutually orthogonal (all dot products near zero)
            double dotThumbIndex = Math.Abs(thumb.DotProduct(indexFinger));
            double dotThumbMiddle = Math.Abs(thumb.DotProduct(middleFinger));
            double dotIndexMiddle = Math.Abs(indexFinger.DotProduct(middleFinger));

            bool areOrthogonal = (dotThumbIndex <= tolerance) &&
                                  (dotThumbMiddle <= tolerance) &&
                                  (dotIndexMiddle <= tolerance);

            // Only test the right-hand rule when the vectors are confirmed orthogonal
            if (!areOrthogonal)
                return false;

            // Compute the dot product of the cross product with the thumb to test the right-hand rule
            XYZ crossProduct = indexFinger.CrossProduct(middleFinger);
            double rightHandTest = crossProduct.DotProduct(thumb);

            // A positive dot product indicates right-hand rule compliance
            return rightHandTest > tolerance;
        }

        /// <summary>
        /// Generates the index-finger direction that satisfies the right-hand rule given the thumb and middle-finger directions
        /// </summary>
        /// <param name="thumb">Thumb direction vector</param>
        /// <param name="middleFinger">Middle-finger direction vector</param>
        /// <param name="tolerance">Orthogonality tolerance (default 1e-6)</param>
        /// <returns>The computed index-finger direction, or null if the input vectors are not orthogonal</returns>
        public static XYZ GenerateIndexFinger(this XYZ thumb, XYZ middleFinger, double tolerance = 1e-6)
        {
            // Normalise the input vectors first
            XYZ normalizedThumb = thumb.Normalize();
            XYZ normalizedMiddleFinger = middleFinger.Normalize();

            // Check that the two vectors are orthogonal (dot product near zero)
            double dotProduct = normalizedThumb.DotProduct(normalizedMiddleFinger);

            // If the absolute dot product exceeds the tolerance, the vectors are not orthogonal
            if (Math.Abs(dotProduct) > tolerance)
            {
                return null;
            }

            // Compute the index-finger direction via cross product, then negate
            XYZ indexFinger = normalizedMiddleFinger.CrossProduct(normalizedThumb).Negate();

            // Return the normalised index-finger direction
            return indexFinger.Normalize();
        }

        /// <summary>
        /// Creates or retrieves a level at the specified elevation
        /// </summary>
        /// <param name="doc">Revit document</param>
        /// <param name="elevation">Level elevation (ft)</param>
        /// <param name="levelName">Level name</param>
        /// <returns></returns>
        public static Level CreateOrGetLevel(this Document doc, double elevation, string levelName)
        {
            // Search for an existing level at the specified elevation
            Level existingLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => Math.Abs(l.Elevation - elevation) < 0.1 / 304.8);

            if (existingLevel != null)
                return existingLevel;

            // Create a new level
            Level newLevel = Level.Create(doc, elevation);
            // Set the level name
            Level namesakeLevel = new FilteredElementCollector(doc)
                 .OfClass(typeof(Level))
                 .Cast<Level>()
                 .FirstOrDefault(l => l.Name == levelName);
            if (namesakeLevel != null)
            {
                levelName = $"{levelName}_{newLevel.Id.GetValue()}";
            }
            newLevel.Name = levelName;

            return newLevel;
        }

        /// <summary>
        /// Finds the level closest to the given height
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="height">Target height (Revit internal units)</param>
        /// <returns>The level closest to the target height, or null if no levels exist in the document</returns>
        public static Level FindNearestLevel(this Document doc, double height)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "doc cannot be null");

            // Use a LINQ query to get the closest level directly
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - height))
                .FirstOrDefault();
        }

        ///// <summary>
        ///// Refreshes the view and introduces an optional delay
        ///// </summary>
        //public static void Refresh(this Document doc, int waitingTime = 0, bool allowOperation = true)
        //{
        //    UIApplication uiApp = new UIApplication(doc.Application);
        //    UIDocument uiDoc = uiApp.ActiveUIDocument;

        //    // Check whether the document can be modified
        //    if (uiDoc.Document.IsModifiable)
        //    {
        //        // Regenerate the model
        //        uiDoc.Document.Regenerate();
        //    }
        //    // Refresh the active view
        //    uiDoc.RefreshActiveView();

        //    // Wait for the specified delay
        //    if (waitingTime != 0)
        //    {
        //        System.Threading.Thread.Sleep(waitingTime);
        //    }

        //    // Allow the UI thread to process pending events
        //    if (allowOperation)
        //    {
        //        System.Windows.Forms.Application.DoEvents();
        //    }
        //}

        /// <summary>
        /// Saves the specified message to a file on the desktop (overwrites by default)
        /// </summary>
        /// <param name="message">Message content to save</param>
        /// <param name="fileName">Target file name</param>
        public static void SaveToDesktop(this string message, string fileName = "temp.json", bool isAppend = false)
        {
            // Ensure the file name has an extension
            if (!Path.HasExtension(fileName))
            {
                fileName += ".txt"; // Append the default .txt extension
            }

            // Resolve the desktop path
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            // Build the full file path
            string filePath = Path.Combine(desktopPath, fileName);

            // Write to the file (overwrite mode)
            using (StreamWriter sw = new StreamWriter(filePath, isAppend))
            {
                sw.WriteLine($"{message}");
            }
        }

    }
}
