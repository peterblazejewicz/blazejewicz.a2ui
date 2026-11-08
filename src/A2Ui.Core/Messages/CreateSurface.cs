using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Components;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>Operation payload for creating a new surface.</summary>
public sealed record CreateSurface
{
    /// <summary>Unique identifier for the surface being created.</summary>
    [JsonPropertyName("surfaceId")]
    public required string SurfaceId { get; init; }

    /// <summary>Catalog identifier defining the default allowed component set for this surface.</summary>
    [JsonPropertyName("catalogId")]
    public string? CatalogId { get; init; }

    /// <summary>Optional theme configuration (primaryColor, iconUrl, agentDisplayName).</summary>
    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Theme { get; init; }

    /// <summary>When true, the client sends data model state back to the server.</summary>
    [JsonPropertyName("sendDataModel")]
    public bool? SendDataModel { get; init; }

    /// <summary>Optional inline components list created atomically with the surface (v1.0).</summary>
    [JsonPropertyName("components")]
    public IReadOnlyList<A2UiComponent>? Components { get; init; }

    /// <summary>Optional initial root data model object for the surface (v1.0).</summary>
    [JsonPropertyName("dataModel")]
    public JsonElement? DataModel { get; init; }

    /// <summary>Optional surface-level metadata (v1.0).</summary>
    [JsonPropertyName("metadata")]
    public SurfaceMetadata? Metadata { get; init; }

    /// <summary>Validates the createSurface payload.</summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(this.SurfaceId))
        {
            throw new A2UiMessageValidationException("Missing required 'surfaceId' property");
        }

        this.Metadata?.Validate();

        if (this.DataModel is { } dm)
        {
            UpdateDataModel.ValidateNoReservedKeys(dm);
        }
    }
}
