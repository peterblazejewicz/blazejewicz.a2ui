using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Capabilities;

/// <summary>
/// A2UI v1.0 renderer data model schema conveying current data models for all active surfaces.
/// Maps to specification/v1_0/json/renderer_data_model.json.
/// </summary>
public sealed record RendererDataModel
{
    /// <summary>Protocol version string (must be "1.0").</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = A2UiProtocol.Version10;

    /// <summary>Map of surface IDs to their current data model objects.</summary>
    [JsonPropertyName("surfaces")]
    public required Dictionary<string, JsonElement> Surfaces { get; init; }
}
