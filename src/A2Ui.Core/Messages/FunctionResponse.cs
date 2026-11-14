using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Return response matching a callAgentFunction or callRendererFunction invocation.
/// Maps to common_types.json#/$defs/FunctionResponse.
/// </summary>
public sealed record FunctionResponse
{
    /// <summary>Unique ID matching the initiating function call.</summary>
    [JsonPropertyName("functionCallId")]
    public required string FunctionCallId { get; init; }

    /// <summary>Return value of the function, mutually exclusive with <see cref="Error"/>.</summary>
    [JsonPropertyName("value")]
    [JsonConverter(typeof(NullableJsonElementConverter))]
    public JsonElement? Value { get; init; }

    /// <summary>Error object indicating failure of the function execution, mutually exclusive with <see cref="Value"/>.</summary>
    [JsonPropertyName("error")]
    public FunctionError? Error { get; init; }

    /// <summary>
    /// Validates that functionCallId is present and exactly one of value or error is provided.
    /// </summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when validation constraints fail.</exception>
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrEmpty(this.FunctionCallId))
        {
            violations.Add("Missing required 'functionCallId' property");
        }

        bool hasValue = this.Value is not null;
        bool hasError = this.Error is not null;

        if (!hasValue && !hasError)
        {
            violations.Add("FunctionResponse must contain exactly one of 'value' or 'error'");
        }
        else if (hasValue && hasError)
        {
            violations.Add("FunctionResponse cannot contain both 'value' and 'error'");
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}

internal sealed class NullableJsonElementConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;

    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            value.Value.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
