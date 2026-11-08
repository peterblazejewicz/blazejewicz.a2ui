using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>Card container with rounded borders.</summary>
public sealed record CardComponent : A2UiComponent
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Component { get; init; } = "Card";
}
