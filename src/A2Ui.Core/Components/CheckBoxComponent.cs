using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>Checkbox input component.</summary>
public sealed record CheckBoxComponent : A2UiComponent
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Component { get; init; } = "CheckBox";

    /// <summary>Current checked state (bound to data model).</summary>
    [JsonPropertyName("value")]
    public DynamicValue? Value { get; init; }
}
