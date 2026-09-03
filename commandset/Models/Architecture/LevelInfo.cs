//
//                       RevitAPI-Solutions
// Copyright (c) Duong Tran Quang (DTDucas) (baymax.contact@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Architecture;

public class LevelInfo
{
    /// <summary>
    ///     Name of the level
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>
    ///     Elevation of the level in millimeters
    /// </summary>
    [JsonProperty("elevation")]
    public double Elevation { get; set; }

    /// <summary>
    ///     Whether this level is a building story
    /// </summary>
    [JsonProperty("isBuildingStory")]
    public bool IsBuildingStory { get; set; } = true;

    /// <summary>
    ///     Whether to create a floor plan view for this level (default: true)
    /// </summary>
    [JsonProperty("createFloorPlan")]
    public bool CreateFloorPlan { get; set; } = true;

    /// <summary>
    ///     Whether to create a ceiling plan view for this level (default: true)
    /// </summary>
    [JsonProperty("createCeilingPlan")]
    public bool CreateCeilingPlan { get; set; } = true;
}
