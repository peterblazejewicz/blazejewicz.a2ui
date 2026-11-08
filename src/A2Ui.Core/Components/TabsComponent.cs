using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Tabs;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>Tabbed container component.</summary>
public sealed record TabsComponent : A2UiComponent
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Component { get; init; } = "Tabs";

    /// <summary>Tab definitions with title and child reference.</summary>
    [JsonPropertyName("tabs")]
    public TabDefinition[]? Tabs { get; init; }
}
