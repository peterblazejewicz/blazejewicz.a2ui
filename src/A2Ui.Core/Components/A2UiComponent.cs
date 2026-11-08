using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Accessibility;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>
/// A2UI component in the flat adjacency list.
/// Deserialized via <see cref="A2UiComponentConverter"/> supporting known Basic Catalog types
/// plus <see cref="GenericComponent"/> for custom/multi-catalog components.
/// </summary>
[JsonConverter(typeof(A2UiComponentConverter))]
public abstract record A2UiComponent
{
    // ── Identity ──────────────────────────────────────────────────────────

    /// <summary>Unique component identifier within the surface.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Catalog identifier for this component, overriding any surface-level default catalogId (v1.0).</summary>
    [JsonPropertyName("catalogId")]
    public string? CatalogId { get; init; }

    /// <summary>Optional component-level metadata for vendor extensions (v1.0).</summary>
    [JsonPropertyName("metadata")]
    public ComponentMetadata? Metadata { get; init; }

    /// <summary>
    /// Component type discriminator. Populated by each sealed subtype's default value.
    /// Ignored by JSON serialization — the <c>[JsonConverter]</c> handles
    /// the wire-format <c>"component"</c> field.
    /// </summary>
    [JsonIgnore]
    public virtual string Component { get; init; } = "";

    // ── Tree structure ────────────────────────────────────────────────────
    /// <summary>Legacy back-reference. v0.9 prefers forward-ref via children/child.</summary>
    [JsonPropertyName("parent")]
    public string? Parent { get; init; }

    /// <summary>Single child component ID (Card, Button, Modal trigger/content).</summary>
    [JsonPropertyName("child")]
    public string? Child { get; init; }

    /// <summary>v0.9 forward-reference children list (static IDs or template).</summary>
    [JsonPropertyName("children")]
    public ChildList? Children { get; init; }

    // ── ComponentCommon (common_types.json) ────────────────────────────────

    /// <summary>Accessibility attributes for screen readers.</summary>
    [JsonPropertyName("accessibility")]
    public AccessibilityAttributes? Accessibility { get; init; }

    /// <summary>Layout weight for proportional sizing within a container.</summary>
    [JsonPropertyName("weight")]
    public double? Weight { get; init; }

    // ── Checkable ─────────────────────────────────────────────────────────

    /// <summary>Client-side validation rules for this component.</summary>
    [JsonPropertyName("checks")]
    public CheckRule[]? Checks { get; init; }

    // ── Display properties ────────────────────────────────────────────────

    /// <summary>Primary text content of the component.</summary>
    [JsonPropertyName("text")]
    public DynamicValue? Text { get; init; }

    /// <summary>Label text displayed alongside or above the component.</summary>
    [JsonPropertyName("label")]
    public DynamicValue? Label { get; init; }

    // ── Extension data ────────────────────────────────────────────────────

    /// <summary>Captures any unrecognized JSON properties for forward compatibility.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
