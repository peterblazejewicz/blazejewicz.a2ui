using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Capabilities;

/// <summary>
/// A2UI v1.0 renderer capabilities — sent from renderer to agent as transport metadata.
/// Maps to specification/v1_0/json/renderer_capabilities.json.
/// </summary>
public sealed record RendererCapabilities
{
    /// <summary>Version 1.0 capabilities structure.</summary>
    [JsonPropertyName("1.0")]
    public required RendererCapabilitiesV10 V10 { get; init; }
}

/// <summary>
/// Renderer capability details for A2UI protocol v1.0.
/// </summary>
public sealed record RendererCapabilitiesV10
{
    /// <summary>String identifiers for each of the catalogs supported by the renderer.</summary>
    [JsonPropertyName("supportedCatalogIds")]
    public required IReadOnlyList<string> SupportedCatalogIds { get; init; }

    /// <summary>Optional inline catalog definitions.</summary>
    [JsonPropertyName("inlineCatalogs")]
    public IReadOnlyList<JsonElement>? InlineCatalogs { get; init; }
}
