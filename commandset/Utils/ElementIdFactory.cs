using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Construction of ElementId values across Revit versions.
    ///
    /// The name matters. "ElementIdExtensions" collides with a Nice3point type in
    /// the same scope; "ElementIds" collides with a PROPERTY of that name on three
    /// handlers (string[] / int[] ElementIds). Both produced a compile error at
    /// every call site, so this one is deliberately unlike either.
    /// </summary>
    public static class ElementIdFactory
    {
        /// <summary>
        /// Creates an ElementId from a numeric value, on every supported Revit version.
        ///
        /// ElementId(int) is marked OBSOLETE from Revit 2024 onward in favour of
        /// ElementId(long), but ElementId(long) does not EXIST before 2024 - so neither
        /// constructor is correct everywhere and 83 call sites cannot each carry a
        /// version guard. This is the one place that branch belongs.
        /// </summary>
#if REVIT2024_OR_GREATER
        public static ElementId Create(long value) => new ElementId(value);
#else
        public static ElementId Create(long value) => new ElementId((int)value);
#endif

    }
}
