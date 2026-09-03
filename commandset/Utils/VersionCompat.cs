using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Cross-version helpers for Revit 2020-2027.
    ///
    /// Every member touched here was resolved against the Revit API documentation
    /// for 2022, 2023, 2024, 2025, 2026 and 2027 rather than assumed. Two things
    /// came out of that:
    ///
    ///   1. Almost nothing in this file needs a version guard. Thirteen of the
    ///      members it used to branch on are IDENTICAL across 2022-2027, and were
    ///      being stubbed to null on 2026 for no reason. Duct.Create, Pipe.Create,
    ///      Conduit.Create, ViewSection.CreateCallout, View.CreateViewTemplate,
    ///      Ceiling.Create, Floor.Create, RevisionCloud.Create and NurbSpline are
    ///      all in that group.
    ///
    ///   2. Eleven members it branched on do not exist in ANY version. They were
    ///      not removed in 2026 - they were never there. ReferencePlane.Create,
    ///      TextNote.Rotation, ViewSchedule.ShowHeaders/ShowGridLines/ShowOutlines,
    ///      ViewSheet.AddRevision, ScheduleDefinition.SetFieldVisibility and
    ///      GetCategoryId, MEPSystem.AddElements, ElevationMarker.CreateElevationView,
    ///      Face.SurfaceType, Category.Parameters, IntersectionResult.Reference and
    ///      Space.Create. Several are near-misses for a real member
    ///      (Dependent -> AsDependent, CreateElevationView -> CreateElevation,
    ///      AddElements -> Add(ConnectorSet), GetCategoryId() -> CategoryId), which
    ///      is the signature of API written from pattern rather than from lookup.
    ///
    /// Each method below therefore either calls the one real API unconditionally,
    /// or carries the single guard the corpus actually justifies. Nothing returns
    /// null to paper over an unsupported version, because after resolution there is
    /// no such version: every operation has a real implementation on 2022-2027.
    /// </summary>
    public static class VersionCompat
    {
        // Identical on 2022-2027. LocationCurve is unchanged across the span.
        public static Curve GetWallLocationCurve(Wall wall)
        {
            return (wall.Location as LocationCurve)?.Curve;
        }

        // ===== RevisionCloud.Create =====
        // Resolved: Create(Document, View, ElementId, IList<Curve>) is IDENTICAL on
        // 2022-2027 (same doc id across all six corpora). The CurveLoop overload and
        // the version-oscillating parameter order this file used to branch on do not
        // exist in any version.
        public static RevisionCloud CreateRevisionCloud(
            Document doc, ElementId revisionId, IList<CurveLoop> loops, ElementId viewId)
        {
            var curves = new List<Curve>();
            foreach (CurveLoop loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    curves.Add(curve);
                }
            }

            View view = doc.GetElement(viewId) as View;
            if (view == null)
            {
                throw new ArgumentException(
                    "CreateRevisionCloud: viewId does not resolve to a View.", nameof(viewId));
            }

            return RevisionCloud.Create(doc, view, revisionId, curves);
        }

        // Revision numbering is a parameter on every supported version.
        public static void SetRevisionNumber(this Revision revision, string number)
        {
            revision.get_Parameter(BuiltInParameter.PROJECT_REVISION_REVISION_NUM).Set(number);
        }

        // ===== Reference planes =====
        // Resolved: ReferencePlane.Create does not exist on any version. The one and
        // only factory is ItemFactoryBase.NewReferencePlane, IDENTICAL on 2022-2027.
        // cutVec must be perpendicular to (freeEnd - bubbleEnd); plane.Normal is,
        // by definition, perpendicular to plane.XVec.
        public static ReferencePlane CreateReferencePlane(Document doc, Plane plane)
        {
            View view = doc.ActiveView;
            if (view == null)
            {
                throw new InvalidOperationException(
                    "CreateReferencePlane: the document has no active view to host the plane.");
            }

            return doc.Create.NewReferencePlane(
                plane.Origin, plane.Origin + plane.XVec, plane.Normal, view);
        }

        // ===== Text note rotation =====
        // Resolved: TextNote has no Rotation property on any version 2022-2027 - the
        // only Rotation in this area is TextNoteOptions.Rotation, which applies at
        // CREATION time. For an element that already exists, ElementTransformUtils is
        // the answer on every version.
        public static void SetTextNoteRotation(Document doc, TextNote textNote, double rotation)
        {
            if (Math.Abs(rotation) < 1e-9)
            {
                return;
            }

            Line axis = Line.CreateUnbound(textNote.Coord, XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, textNote.Id, axis, rotation * Math.PI / 180.0);
        }

        // ===== Schedule appearance =====
        // Resolved: ShowHeaders and ShowGridLines are properties of ScheduleDefinition,
        // NOT of ViewSchedule, and they exist on every version. ShowOutlines exists on
        // neither type in any version, so the method that used to set it is gone rather
        // than silently mapped onto a different property.
        public static void SetScheduleShowHeaders(ViewSchedule schedule, bool show)
        {
            schedule.Definition.ShowHeaders = show;
        }

        public static void SetScheduleShowGridLines(ViewSchedule schedule, bool show)
        {
            schedule.Definition.ShowGridLines = show;
        }

        // ===== Splines =====
        // Resolved: NurbSpline exposes CreateCurve, not Create, and CreateCurve
        // (HermiteSpline) is present on 2022-2027.
        public static Curve CreateNurbSpline(IList<XYZ> points)
        {
            HermiteSpline hermite = HermiteSpline.Create(points, false);
            return NurbSpline.CreateCurve(hermite);
        }

        // ===== View duplication =====
        // Resolved: the enum member has ALWAYS been AsDependent, on every version
        // (Duplicate = 0, AsDependent = 1, WithDetailing = 2). There is no member
        // named Dependent in any version, which is why the old code could not compile
        // once its guard stopped hiding it.
        public static ViewDuplicateOption GetDependentDuplicateOption()
        {
            return ViewDuplicateOption.AsDependent;
        }

        // ===== Ceilings and floors =====
        // Resolved: both Create overloads are present and identical on 2022-2027, so
        // the old code's "return null below 2023" was wrong for 2022.
        //
        // 2020 and 2021 are a separate matter and NOT covered by that resolution: the
        // API documentation corpora consulted start at 2022, and the compiler confirms
        // neither Ceiling.Create nor Floor.Create exists on those two. Guarded at the
        // lowest symbol the project defines, and refusing loudly rather than returning
        // null - a caller that gets null here would go on to dereference it.
#if REVIT2022_OR_GREATER
        public static Ceiling CreateCeiling(
            Document doc, IList<CurveLoop> profile, ElementId ceilingTypeId, ElementId levelId)
        {
            return Ceiling.Create(doc, profile, ceilingTypeId, levelId);
        }

        public static Floor CreateFloor(
            Document doc, IList<CurveLoop> profile, ElementId floorTypeId, ElementId levelId)
        {
            return Floor.Create(doc, profile, floorTypeId, levelId);
        }
#else
        public static Ceiling CreateCeiling(
            Document doc, IList<CurveLoop> profile, ElementId ceilingTypeId, ElementId levelId)
        {
            throw new NotSupportedException(
                "Ceiling.Create was introduced in Revit 2022; this build targets 2020/2021, where " +
                "ceilings can only be created through the Revit UI.");
        }

        public static Floor CreateFloor(
            Document doc, IList<CurveLoop> profile, ElementId floorTypeId, ElementId levelId)
        {
            throw new NotSupportedException(
                "Floor.Create(Document, IList<CurveLoop>, ElementId, ElementId) was introduced in " +
                "Revit 2022; this build targets 2020/2021, where the replaced NewFloor factory takes " +
                "a different profile type. Create the floor through the Revit UI.");
        }
#endif

        // ===== Spaces =====
        // Resolved: Space.Create does not exist on any version. The factory is
        // Creation.Document.NewSpace(Level, UV), present on 2022-2027.
        public static Space CreateSpace(Document doc, ElementId levelId, XYZ point)
        {
            Level level = doc.GetElement(levelId) as Level;
            if (level == null)
            {
                throw new ArgumentException(
                    "CreateSpace: levelId does not resolve to a Level.", nameof(levelId));
            }

            return doc.Create.NewSpace(level, new UV(point.X, point.Y));
        }

        // ===== MEP systems =====
        // Resolved: MEPSystem.AddElements does not exist on any version. The real
        // member is Add(ConnectorSet), IDENTICAL on 2022-2027. The old code no-opped
        // this entirely on 2026, silently dropping every element it claimed to add.
        public static void AddElementsToMEPSystem(
            Document doc, MEPSystem system, IList<ElementId> elementIds)
        {
            ConnectorSet connectors = new ConnectorSet();

            foreach (ElementId id in elementIds)
            {
                Element element = doc.GetElement(id);
                ConnectorManager manager = GetConnectorManager(element);
                if (manager == null)
                {
                    continue;
                }

                foreach (Connector connector in manager.Connectors)
                {
                    if (!connector.IsConnected)
                    {
                        connectors.Insert(connector);
                    }
                }
            }

            if (connectors.IsEmpty)
            {
                throw new InvalidOperationException(
                    "AddElementsToMEPSystem: none of the supplied elements exposed a free connector, " +
                    "so there is nothing MEPSystem.Add could attach. Check that the elements are MEP " +
                    "curves or family instances with an unconnected connector of the system's domain.");
            }

            system.Add(connectors);
        }

        private static ConnectorManager GetConnectorManager(Element element)
        {
            if (element is MEPCurve curve)
            {
                return curve.ConnectorManager;
            }

            if (element is FamilyInstance instance)
            {
                return instance.MEPModel?.ConnectorManager;
            }

            return null;
        }

        // ===== Elevation views =====
        // Resolved: the member is CreateElevation, not CreateElevationView, and it
        // takes the id of a ViewPlan the marker is visible in - not a Level id. It is
        // IDENTICAL on 2022-2027 and never returns null; it throws.
        public static ViewSection CreateElevationView(
            Document doc, ElevationMarker marker, ElementId levelId, int index)
        {
            ElementId viewPlanId = FindViewPlanForLevel(doc, levelId);
            if (viewPlanId == ElementId.InvalidElementId)
            {
                throw new InvalidOperationException(
                    "CreateElevationView: no ViewPlan was found for the requested level, and " +
                    "ElevationMarker.CreateElevation requires one to inherit extents from. " +
                    "Create or open a plan view on that level first.");
            }

            return marker.CreateElevation(doc, viewPlanId, index);
        }

        // Prefer a plan view generated by the requested level; fall back to the active
        // view when it is itself a plan.
        private static ElementId FindViewPlanForLevel(Document doc, ElementId levelId)
        {
            ViewPlan match = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == levelId);

            if (match != null)
            {
                return match.Id;
            }

            if (doc.ActiveView is ViewPlan activePlan && !activePlan.IsTemplate)
            {
                return activePlan.Id;
            }

            return ElementId.InvalidElementId;
        }

        // ===== Callouts =====
        // Resolved: CreateCallout takes two XYZ corner points, not a BoundingBoxXYZ,
        // and returns View (which may be a ViewSection, ViewPlan or ViewDetail). It is
        // IDENTICAL on 2022-2027 and never returns null.
        public static View CreateCallout(
            Document doc, ElementId hostViewId, ElementId viewFamilyTypeId, BoundingBoxXYZ box)
        {
            return ViewSection.CreateCallout(doc, hostViewId, viewFamilyTypeId, box.Min, box.Max);
        }

        // ===== View templates =====
        // Resolved: CreateViewTemplate is an INSTANCE method taking no arguments and
        // returning a View. The old two-argument static form does not exist, and the
        // old code's InvalidElementId return had no basis on any version.
        public static ElementId CreateViewTemplate(Document doc, ElementId sourceViewId)
        {
            View source = doc.GetElement(sourceViewId) as View;
            if (source == null)
            {
                throw new ArgumentException(
                    "CreateViewTemplate: sourceViewId does not resolve to a View.", nameof(sourceViewId));
            }

            View template = source.CreateViewTemplate();
            return template?.Id ?? ElementId.InvalidElementId;
        }

        // ===== MEP curves =====
        // Resolved: Duct.Create, Pipe.Create and Conduit.Create are each present with
        // the same overload set on 2022-2027. The old code returned null on 2026.
        // Signature resolved: Create(Document, systemTypeId, curveTypeId, levelId, start, end).
        // Both id arguments must name a real type; callers routinely pass
        // InvalidElementId meaning "whatever the project has", so resolve it here
        // rather than letting Revit throw a vaguer ArgumentException deeper in.
        public static Duct CreateDuct(
            Document doc, ElementId systemTypeId, XYZ start, XYZ end, ElementId levelId)
        {
            return Duct.Create(
                doc,
                Resolve<MechanicalSystemType>(doc, systemTypeId),
                Resolve<DuctType>(doc, ElementId.InvalidElementId),
                levelId, start, end);
        }

        public static Pipe CreatePipe(
            Document doc, ElementId systemTypeId, XYZ start, XYZ end, ElementId levelId)
        {
            return Pipe.Create(
                doc,
                Resolve<PipingSystemType>(doc, systemTypeId),
                Resolve<PipeType>(doc, ElementId.InvalidElementId),
                levelId, start, end);
        }

        // Conduit.Create documents InvalidElementId as "use the document default" for
        // both the type and the level, so it is passed straight through.
        public static Conduit CreateConduit(
            Document doc, ElementId conduitTypeId, XYZ start, XYZ end, ElementId levelId)
        {
            return Conduit.Create(doc, conduitTypeId ?? ElementId.InvalidElementId, start, end, levelId);
        }

        // Keep a caller-supplied id when it names a real element of the wanted type;
        // otherwise fall back to the first one in the project. Fails loudly when the
        // project has none, because that is a project-content problem the caller can
        // act on, not something to paper over with a null return.
        private static ElementId Resolve<T>(Document doc, ElementId supplied) where T : ElementType
        {
            if (supplied != null && supplied != ElementId.InvalidElementId && doc.GetElement(supplied) is T)
            {
                return supplied;
            }

            T type = new FilteredElementCollector(doc)
                .OfClass(typeof(T))
                .Cast<T>()
                .FirstOrDefault();

            if (type == null)
            {
                throw new InvalidOperationException(
                    "This project contains no " + typeof(T).Name + ", so the element cannot be created. " +
                    "Load or define one and retry.");
            }

            return type.Id;
        }

        // ===== Categories =====
        // Resolved: Category.BuiltInCategory was added in 2023. Before that, the id
        // cast is the only route. This is one of the two guards in this file that the
        // corpus actually justifies.
        public static BuiltInCategory GetBuiltInCategory(Category category)
        {
#if REVIT2023_OR_GREATER
            return category.BuiltInCategory;
#else
            return (BuiltInCategory)category.Id.GetIntValue();
#endif
        }

        // ===== Sheet revisions =====
        // Resolved: ViewSheet.AddRevision does not exist on any version. The documented
        // pattern is GetAdditionalRevisionIds -> add -> SetAdditionalRevisionIds, both
        // present since 2015. The old code no-opped on 2026 and dropped the revision.
        public static void AddRevisionToSheet(ViewSheet sheet, ElementId revisionId)
        {
            // GetAdditionalRevisionIds returns ICollection<ElementId>; SetAdditionalRevisionIds
            // takes ICollection<ElementId>. Copy into a list so the add is on a collection we
            // own rather than on whatever Revit handed back.
            ICollection<ElementId> current = sheet.GetAdditionalRevisionIds();
            if (current.Contains(revisionId))
            {
                return;
            }

            List<ElementId> ids = new List<ElementId>(current) { revisionId };
            sheet.SetAdditionalRevisionIds(ids);
        }

        // ===== Schedule fields =====
        // Resolved: ScheduleDefinition has no SetFieldVisibility and no GetCategoryId.
        // Visibility lives on the field itself (ScheduleField.IsHidden) and the category
        // is the CategoryId PROPERTY. Both are present on every version.
        public static void SetScheduleFieldVisibility(
            ScheduleDefinition definition, ScheduleFieldId fieldId, bool visible)
        {
            ScheduleField field = definition.GetField(fieldId);
            if (field == null)
            {
                throw new ArgumentException(
                    "SetScheduleFieldVisibility: the schedule has no field with that id.", nameof(fieldId));
            }

            field.IsHidden = !visible;
        }

        public static ElementId GetScheduleCategoryId(ScheduleDefinition definition)
        {
            return definition.CategoryId;
        }

        // ===== Category parameters =====
        // Resolved: Category.Parameters does not exist on any version - the Category
        // surface is 15 properties in 2022 and 17 in 2027, and Parameters is in neither.
        // The caller's real question is "which parameters do elements of this category
        // carry", which is answered by reading an exemplar element of that category.
        // Returns empty when the model contains no such element, which is the honest
        // answer rather than a fabricated one.
        public static IEnumerable<Parameter> GetCategoryParameters(Document doc, Category category)
        {
            if (category == null)
            {
                return Enumerable.Empty<Parameter>();
            }

            Element exemplar = new FilteredElementCollector(doc)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType()
                .FirstElement();

            if (exemplar == null)
            {
                return Enumerable.Empty<Parameter>();
            }

            return exemplar.Parameters.Cast<Parameter>();
        }

        // ===== Faces =====
        // Resolved: Face.SurfaceType does not exist on any version - the Face surface is
        // the same 9 properties on 2022 and 2027, and SurfaceType is in none of them.
        // Testing the concrete subclass is the stable idiom across the whole span.
        public static string GetSurfaceTypeName(Face face)
        {
            if (face is PlanarFace) return "Planar";
            if (face is CylindricalFace) return "Cylindrical";
            if (face is ConicalFace) return "Conical";
            if (face is RevolvedFace) return "Revolved";
            if (face is RuledFace) return "Ruled";
            if (face is HermiteFace) return "Hermite";
            return "Unknown";
        }

        // ===== Face references =====
        // Resolved: IntersectionResult has 6 properties (Distance, EdgeObject,
        // EdgeParameter, Parameter, UVPoint, XYZPoint) and Reference is not among them
        // on any version. A reference to the projected face comes from the FACE, whose
        // Reference property is present on 2022-2027.
        public static Reference GetFaceReference(Face face)
        {
            return face?.Reference;
        }

        // Pure string mapping, no API surface involved.
        public static string GetDisplayStyleName(string styleName)
        {
            switch (styleName.ToLower())
            {
                case "wireframe": return "Wireframe";
                case "hidden":
                case "hiddenline": return "HiddenLine";
                case "shaded":
                case "shading": return "Shading";
                case "consistent_colors": return "ConsistentColors";
                case "realistic": return "Realistic";
                default: return "HiddenLine";
            }
        }
    }
}
