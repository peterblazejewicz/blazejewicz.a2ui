using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Accessibility;

/// <summary>Accessibility attributes for screen readers.</summary>
public sealed record AccessibilityAttributes
{
    /// <summary>Accessible label text for screen readers.</summary>
    [JsonPropertyName("label")]
    public DynamicValue? Label { get; init; }

    /// <summary>Accessible description text for screen readers.</summary>
    [JsonPropertyName("description")]
    public DynamicValue? Description { get; init; }

    /// <summary>Screen reader live region mode: "off", "polite", or "assertive" (v1.0).</summary>
    [JsonPropertyName("live")]
    public string? Live { get; init; }

    /// <summary>Whether the element is hidden from screen readers (v1.0).</summary>
    [JsonPropertyName("hidden")]
    public DynamicValue? Hidden { get; init; }
}
