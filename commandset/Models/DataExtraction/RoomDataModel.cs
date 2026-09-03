using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.DataExtraction
{
    /// <summary>
    /// Model for room data extraction.
    /// </summary>
    /// <remarks>
    /// UNITS ARE IN THE KEY NAMES. These values used to be serialised as "area",
    /// "volume", "perimeter" holding Revit's INTERNAL units (square feet, cubic
    /// feet, feet) while every tool description promised millimetres, so an AI
    /// reading 10.76 for a 1 m² room had no way to know. The handler now converts
    /// to metric and the JSON keys say which unit they carry.
    /// </remarks>
    public class RoomDataModel
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("uniqueId")]
        public string UniqueId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }

        /// <summary>Square metres.</summary>
        [JsonProperty("areaM2")]
        public double Area { get; set; }

        /// <summary>Cubic metres.</summary>
        [JsonProperty("volumeM3")]
        public double Volume { get; set; }

        /// <summary>Millimetres.</summary>
        [JsonProperty("perimeterMm")]
        public double Perimeter { get; set; }

        /// <summary>Millimetres.</summary>
        [JsonProperty("unboundedHeightMm")]
        public double UnboundedHeight { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("comments")]
        public string Comments { get; set; }

        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("occupancy")]
        public string Occupancy { get; set; }

        /// <summary>
        /// "placed", "unplaced" (no location) or "not enclosed" (placed but zero area).
        /// Present so a caller who asked for unplaced or unenclosed rooms can tell
        /// them apart from the placed ones in the same list.
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// Result container for room data export
    /// </summary>
    public class ExportRoomDataResult
    {
        [JsonProperty("totalRooms")]
        public int TotalRooms { get; set; }

        /// <summary>Square metres.</summary>
        [JsonProperty("totalAreaM2")]
        public double TotalArea { get; set; }

        [JsonProperty("rooms")]
        public List<RoomDataModel> Rooms { get; set; } = new List<RoomDataModel>();

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
