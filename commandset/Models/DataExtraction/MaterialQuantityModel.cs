using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.DataExtraction
{
    /// <summary>
    /// Model for material quantity data.
    /// </summary>
    /// <remarks>
    /// UNITS ARE IN THE KEY NAMES. "area" and "volume" used to carry Revit's
    /// internal square feet and cubic feet while the tool promised metric; the
    /// handler now converts and the keys say so.
    /// </remarks>
    public class MaterialQuantityModel
    {
        [JsonProperty("materialId")]
        public long MaterialId { get; set; }

        [JsonProperty("materialName")]
        public string MaterialName { get; set; }

        [JsonProperty("materialClass")]
        public string MaterialClass { get; set; }

        /// <summary>Square metres.</summary>
        [JsonProperty("areaM2")]
        public double Area { get; set; }

        /// <summary>Cubic metres.</summary>
        [JsonProperty("volumeM3")]
        public double Volume { get; set; }

        [JsonProperty("elementCount")]
        public int ElementCount { get; set; }

        [JsonProperty("elementIds")]
        public List<long> ElementIds { get; set; } = new List<long>();
    }

    /// <summary>
    /// Result container for material quantities
    /// </summary>
    public class GetMaterialQuantitiesResult
    {
        [JsonProperty("totalMaterials")]
        public int TotalMaterials { get; set; }

        /// <summary>Square metres.</summary>
        [JsonProperty("totalAreaM2")]
        public double TotalArea { get; set; }

        /// <summary>Cubic metres.</summary>
        [JsonProperty("totalVolumeM3")]
        public double TotalVolume { get; set; }

        [JsonProperty("materials")]
        public List<MaterialQuantityModel> Materials { get; set; } = new List<MaterialQuantityModel>();

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
