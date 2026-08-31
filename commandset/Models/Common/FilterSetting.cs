using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// Filter settings - supports combined condition filtering
    /// </summary>
    public class FilterSetting
    {
        /// <summary>
        /// Gets or sets the Revit built-in category name to filter (e.g., "OST_Walls").
        /// If null or empty, no category filter is applied.
        /// </summary>
        [JsonProperty("filterCategory")]
        public string FilterCategory { get; set; } = null;
        /// <summary>
        /// Gets or sets the Revit element type name to filter (e.g., "Wall" or "Autodesk.Revit.DB.Wall").
        /// If null or empty, no type filter is applied.
        /// </summary>
        [JsonProperty("filterElementType")]
        public string FilterElementType { get; set; } = null;
        /// <summary>
        /// Gets or sets the ElementId of the family symbol (FamilySymbol) to filter.
        /// If 0 or negative, no family filter is applied.
        /// Note: this filter applies only to element instances, not to type elements.
        /// </summary>
        [JsonProperty("filterFamilySymbolId")]
        public int FilterFamilySymbolId { get; set; } = -1;
        /// <summary>
        /// Gets or sets whether to include element types (e.g., wall types, door types)
        /// </summary>
        [JsonProperty("includeTypes")]
        public bool IncludeTypes { get; set; } = false;
        /// <summary>
        /// Gets or sets whether to include element instances (e.g., placed walls, doors)
        /// </summary>
        [JsonProperty("includeInstances")]
        public bool IncludeInstances { get; set; } = true;
        /// <summary>
        /// Gets or sets whether to return only elements visible in the current view.
        /// Note: this filter applies only to element instances, not to type elements.
        /// </summary>
        [JsonProperty("filterVisibleInCurrentView")]
        public bool FilterVisibleInCurrentView { get; set; }
        /// <summary>
        /// Gets or sets the minimum bounding point for spatial range filtering (unit: mm).
        /// If this and BoundingBoxMax are set, elements intersecting this bounding box are returned.
        /// </summary>
        [JsonProperty("boundingBoxMin")]
        public JZPoint BoundingBoxMin { get; set; } = null;
        /// <summary>
        /// Gets or sets the maximum bounding point for spatial range filtering (unit: mm).
        /// If this and BoundingBoxMin are set, elements intersecting this bounding box are returned.
        /// </summary>
        [JsonProperty("boundingBoxMax")]
        public JZPoint BoundingBoxMax { get; set; } = null;
        /// <summary>
        /// Maximum element count limit
        /// </summary>
        [JsonProperty("maxElements")]
        public int MaxElements { get; set; } = 50; 
        /// <summary>
        /// Validates the filter settings and checks for potential conflicts.
        /// </summary>
        /// <returns>True if settings are valid; false otherwise.</returns>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            // Check that at least one element kind is selected
            if (!IncludeTypes && !IncludeInstances)
            {
                errorMessage = "Invalid filter settings: must include at least one of element types or element instances";
                return false;
            }

            // Check that at least one filter condition is specified
            if (string.IsNullOrWhiteSpace(FilterCategory) &&
                string.IsNullOrWhiteSpace(FilterElementType) &&
                FilterFamilySymbolId <= 0)
            {
                errorMessage = "Invalid filter settings: must specify at least one filter condition (category, element type, or family symbol)";
                return false;
            }

            // Check for conflicts between type-element mode and certain filters
            if (IncludeTypes && !IncludeInstances)
            {
                List<string> invalidFilters = new List<string>();
                if (FilterFamilySymbolId > 0)
                    invalidFilters.Add("family symbol filter");
                if (FilterVisibleInCurrentView)
                    invalidFilters.Add("view visibility filter");
                if (invalidFilters.Count > 0)
                {
                    errorMessage = $"The following filters do not apply when filtering type elements only: {string.Join(", ", invalidFilters)}";
                    return false;
                }
            }
            // Validate the spatial bounding box filter
            if (BoundingBoxMin != null && BoundingBoxMax != null)
            {
                // Ensure the minimum point is less than or equal to the maximum point
                if (BoundingBoxMin.X > BoundingBoxMax.X ||
                    BoundingBoxMin.Y > BoundingBoxMax.Y ||
                    BoundingBoxMin.Z > BoundingBoxMax.Z)
                {
                    errorMessage = "Invalid bounding box filter: the minimum point coordinates must be less than or equal to the maximum point coordinates";
                    return false;
                }
            }
            else if (BoundingBoxMin != null || BoundingBoxMax != null)
            {
                errorMessage = "Invalid bounding box filter: both the minimum and maximum point coordinates must be set";
                return false;
            }
            return true;
        }
    }
}
