using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Error payload returned within a <see cref="FunctionResponse"/> indicating failure of function execution.
/// Maps to common_types.json#/$defs/FunctionResponse/properties/error.
/// </summary>
public sealed record FunctionError
{
    /// <summary>Error code indicating the reason for function failure.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Human-readable error description.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
