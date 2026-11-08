using System.Text.Json;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>
/// Represents an arbitrary or custom catalog component not defined in the basic catalog.
/// Used for multi-catalog component resolution and forward compatibility.
/// </summary>
public sealed record GenericComponent : A2UiComponent
{
    /// <summary>The component type name.</summary>
    public required string ComponentType { get; init; }

    /// <summary>All raw JSON properties for this component.</summary>
    public IReadOnlyDictionary<string, JsonElement> Properties { get; init; } = new Dictionary<string, JsonElement>();

    /// <inheritdoc/>
    public override string Component => this.ComponentType;
}
