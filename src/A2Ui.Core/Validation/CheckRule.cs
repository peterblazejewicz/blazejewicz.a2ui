using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Validation;

/// <summary>Client-side validation rule.</summary>
public sealed record CheckRule
{
    /// <summary>Condition expression that must evaluate to true for the check to pass.</summary>
    [JsonPropertyName("condition")]
    public required DynamicValue Condition { get; init; }

    /// <summary>Optional error message displayed when the check fails (used as fallback if condition does not return a ValidationResult message).</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
