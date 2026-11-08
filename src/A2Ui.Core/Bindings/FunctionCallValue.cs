using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Bindings;

/// <summary>
/// Invokes a named function on the client or agent.
/// Maps to common_types.json#/$defs/FunctionCall.
/// </summary>
[JsonConverter(typeof(FunctionCallValueConverter))]
public sealed record FunctionCallValue
{
    /// <summary>Name of the function to invoke (e.g. "@call" in v1.0 or "call" in v0.9).</summary>
    public required string Call { get; init; }

    /// <summary>Catalog identifier for this function, overriding any surface-level default catalogId (v1.0).</summary>
    public string? CatalogId { get; init; }

    /// <summary>Optional named arguments passed to the function.</summary>
    public Dictionary<string, JsonElement>? Args { get; init; }

    /// <summary>Optional expected return type hint (e.g., "string", "boolean").</summary>
    public string? ReturnType { get; init; }
}

internal sealed class FunctionCallValueConverter : JsonConverter<FunctionCallValue>
{
    private static readonly HashSet<string> s_validReturnTypes = new(StringComparer.Ordinal)
    {
        "string",
        "number",
        "boolean",
        "array",
        "object",
        "any",
    };

    public override FunctionCallValue? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        bool isV10 = DynamicValueConverter.CurrentProtocolVersion.Value == A2UiProtocol.Version10;
        string? call = null;

        if (isV10)
        {
            if (root.TryGetProperty("call", out _))
            {
                throw new JsonException("Legacy 'call' property is forbidden in v1.0; use '@call'.");
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name is not ("@call" or "args" or "catalogId" or "returnType"))
                {
                    throw new JsonException(
                        $"FunctionCall with '@call' cannot contain unexpected property '{prop.Name}'."
                    );
                }
            }

            if (
                !root.TryGetProperty("@call", out var atCall)
                || atCall.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(atCall.GetString())
            )
            {
                throw new JsonException("FunctionCall missing required '@call' property in v1.0.");
            }

            call = atCall.GetString()!;
            if (call != "@index")
            {
                Uax31Validator.ValidateIdentifier(call, "Function name");
            }
        }
        else
        {
            if (root.TryGetProperty("@call", out _))
            {
                throw new JsonException("v1.0 '@call' property is forbidden in v0.9/v0.9.1; use 'call'.");
            }

            if (
                !root.TryGetProperty("call", out var legCall)
                || legCall.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(legCall.GetString())
            )
            {
                throw new JsonException("FunctionCall missing required 'call' property in v0.9/v0.9.1.");
            }

            call = legCall.GetString()!;
        }

        string? catalogId = root.TryGetProperty("catalogId", out var cat) ? cat.GetString() : null;
        string? returnType = root.TryGetProperty("returnType", out var ret) ? ret.GetString() : null;

        if (isV10 && returnType is not null && !s_validReturnTypes.Contains(returnType))
        {
            throw new JsonException($"Invalid returnType '{returnType}'. Expected string, number, boolean, array, object, or any.");
        }

        Dictionary<string, JsonElement>? args = null;
        if (root.TryGetProperty("args", out var argsEl))
        {
            if (argsEl.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("FunctionCall args property must be a JSON object.");
            }

            args = [];
            foreach (var prop in argsEl.EnumerateObject())
            {
                if (isV10)
                {
                    Uax31Validator.ValidateIdentifier(prop.Name, "FunctionCall argument name");
                }

                args[prop.Name] = prop.Value.Clone();
            }
        }

        return new FunctionCallValue
        {
            Call = call,
            CatalogId = catalogId,
            ReturnType = returnType,
            Args = args,
        };
    }

    public override void Write(Utf8JsonWriter writer, FunctionCallValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        bool isV10 =
            DynamicValueConverter.CurrentProtocolVersion.Value == A2UiProtocol.Version10
            || value.Call.StartsWith('@')
            || !string.IsNullOrEmpty(value.CatalogId);

        writer.WriteString(isV10 ? "@call" : "call", value.Call);

        if (!string.IsNullOrEmpty(value.CatalogId))
        {
            writer.WriteString("catalogId", value.CatalogId);
        }

        if (value.Args is not null)
        {
            writer.WritePropertyName("args");
            JsonSerializer.Serialize(writer, value.Args, options);
        }

        if (!string.IsNullOrEmpty(value.ReturnType))
        {
            writer.WriteString("returnType", value.ReturnType);
        }

        writer.WriteEndObject();
    }
}
