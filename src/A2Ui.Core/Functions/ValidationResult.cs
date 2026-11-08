using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Structured validation result returned by validation functions in A2UI v1.0.
/// Maps to $defs/ValidationResult in specification/v1_0/json/common_types.json.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>True when the validation succeeded, false when validation failed.</summary>
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    /// <summary>Optional machine-readable error or warning code.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Optional human-readable validation message describing the failure.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Severity of the validation failure: "error" (default), "warning", or "info".
    /// Non-error severities display messages without blocking form submission or component validity.
    /// </summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "error";
}
