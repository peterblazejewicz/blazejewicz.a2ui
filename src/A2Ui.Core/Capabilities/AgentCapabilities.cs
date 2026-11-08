using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Capabilities;

/// <summary>
/// A2UI v1.0 agent capabilities — advertised by an A2UI agent to renderers.
/// Maps to specification/v1_0/json/agent_capabilities.json.
/// </summary>
public sealed record AgentCapabilities
{
    /// <summary>Version 1.0 capabilities structure.</summary>
    [JsonPropertyName("1.0")]
    public required AgentCapabilitiesV10 V10 { get; init; }
}

/// <summary>
/// Capability details for A2UI protocol v1.0.
/// </summary>
public sealed record AgentCapabilitiesV10
{
    /// <summary>IDs identifying catalogs for which the agent can generate content.</summary>
    [JsonPropertyName("supportedCatalogIds")]
    public IReadOnlyList<string>? SupportedCatalogIds { get; init; }

    /// <summary>Indicates if the agent can accept an 'inlineCatalogs' array in renderer capabilities.</summary>
    [JsonPropertyName("acceptsInlineCatalogs")]
    public bool? AcceptsInlineCatalogs { get; init; }
}
