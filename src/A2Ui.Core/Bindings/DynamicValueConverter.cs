using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Bindings;

/// <summary>
/// JSON converter for <see cref="DynamicValue"/>.
/// Supports v0.9 (path, call) and v1.0 (@path, @call, @@ unescaping, single-@ rejection, plain object literals).
/// </summary>
public sealed class DynamicValueConverter : JsonConverter<DynamicValue>
{
    /// <summary>Current protocol version active for deserialization context.</summary>
    internal static readonly AsyncLocal<string?> CurrentProtocolVersion = new();

    private static readonly HashSet<string> s_validReturnTypes = new(StringComparer.Ordinal)
    {
        "string",
        "number",
        "boolean",
        "array",
        "object",
        "any",
    };

    /// <summary>Enters a scoped protocol version context for the duration of the returned disposable.</summary>
    public static IDisposable EnterVersionScope(string version) => new VersionScope(version);

    /// <inheritdoc />
    public override DynamicValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => new DynamicValue.StringValue(reader.GetString()!),
            JsonTokenType.Number => new DynamicValue.NumberValue(reader.GetDouble()),
            JsonTokenType.True => new DynamicValue.BoolValue(true),
            JsonTokenType.False => new DynamicValue.BoolValue(false),
            JsonTokenType.StartArray => new DynamicValue.ArrayValue(JsonElement.ParseValue(ref reader)),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException(
                $"DynamicValue cannot be read from JSON token '{reader.TokenType}'. Expected string, number, boolean, array, or object."
            ),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DynamicValue value, JsonSerializerOptions options)
    {
        value.Switch(
            onString: s => writer.WriteStringValue(s.Value),
            onNumber: n => writer.WriteNumberValue(n.Value),
            onBool: b => writer.WriteBooleanValue(b.Value),
            onArray: a => a.Value.WriteTo(writer),
            onPath: p =>
            {
                writer.WriteStartObject();
                bool isV10 = CurrentProtocolVersion.Value == A2UiProtocol.Version10;
                writer.WriteString(isV10 ? "@path" : "path", p.DataPath);
                writer.WriteEndObject();
            },
            onFunction: f => JsonSerializer.Serialize(writer, f.Call, options),
            onObject: o => WriteObjectValue(writer, o.Value, options)
        );
    }

    private static DynamicValue ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        bool isV10 = CurrentProtocolVersion.Value == A2UiProtocol.Version10;

        if (isV10)
        {
            // 1. v1.0 @path binding
            if (root.TryGetProperty("@path", out var atPathEl))
            {
                int propCount = 0;
                foreach (var _ in root.EnumerateObject())
                {
                    propCount++;
                }

                if (propCount > 1)
                {
                    throw new JsonException("DataBinding with '@path' cannot contain additional properties.");
                }

                if (atPathEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(atPathEl.GetString()))
                {
                    throw new JsonException("DataBinding '@path' must be a non-empty string.");
                }

                return new DynamicValue.PathValue(atPathEl.GetString()!);
            }

            // 2. v1.0 @call function call
            if (root.TryGetProperty("@call", out var atCallEl))
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name is not ("@call" or "args" or "catalogId" or "returnType"))
                    {
                        throw new JsonException(
                            $"FunctionCall with '@call' cannot contain unexpected property '{prop.Name}'."
                        );
                    }
                }

                if (atCallEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(atCallEl.GetString()))
                {
                    throw new JsonException("FunctionCall '@call' property must be a non-empty string.");
                }

                string callName = atCallEl.GetString()!;
                if (callName != "@index" && !Uax31Validator.IsValidIdentifier(callName))
                {
                    throw new JsonException(
                        $"Function name '{callName}' does not conform to UAX 31 identifier syntax."
                    );
                }

                string? catalogId = root.TryGetProperty("catalogId", out var catEl) ? catEl.GetString() : null;
                string? returnType = root.TryGetProperty("returnType", out var retEl) ? retEl.GetString() : null;

                if (returnType is not null && !s_validReturnTypes.Contains(returnType))
                {
                    throw new JsonException(
                        $"Invalid returnType '{returnType}'. Expected string, number, boolean, array, object, or any."
                    );
                }

                Dictionary<string, JsonElement>? args = null;
                if (root.TryGetProperty("args", out var argsEl))
                {
                    if (argsEl.ValueKind != JsonValueKind.Object)
                    {
                        throw new JsonException("FunctionCall args must be a JSON object.");
                    }

                    args = [];
                    foreach (var argProp in argsEl.EnumerateObject())
                    {
                        if (!Uax31Validator.IsValidIdentifier(argProp.Name))
                        {
                            throw new JsonException(
                                $"FunctionCall argument name '{argProp.Name}' does not conform to UAX 31 identifier syntax."
                            );
                        }

                        args[argProp.Name] = argProp.Value.Clone();
                    }
                }

                return new DynamicValue.FunctionValue(
                    new FunctionCallValue
                    {
                        Call = callName,
                        CatalogId = catalogId,
                        ReturnType = returnType,
                        Args = args,
                    }
                );
            }

            // 3. Plain object literal in v1.0
            var dict = ParseAndValidatePlainObject(root);
            return new DynamicValue.ObjectValue(dict);
        }

        // v0.9 / v0.9.1 mode
        if (root.TryGetProperty("@path", out _) || root.TryGetProperty("@call", out _))
        {
            throw new JsonException("Directives '@path' and '@call' are not allowed in v0.9/v0.9.1 protocol payloads.");
        }

        if (root.TryGetProperty("path", out var pathEl))
        {
            if (pathEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(pathEl.GetString()))
            {
                throw new JsonException("DataBinding 'path' must be a non-empty string.");
            }

            return new DynamicValue.PathValue(pathEl.GetString()!);
        }

        if (root.TryGetProperty("call", out _))
        {
            var fc = root.Deserialize<FunctionCallValue>(options);
            return new DynamicValue.FunctionValue(fc!);
        }

        if (root.TryGetProperty("svgPath", out _))
        {
            var dict = ParseAndValidatePlainObject(root);
            return new DynamicValue.ObjectValue(dict);
        }

        throw new JsonException(
            "Invalid DynamicValue object: expected 'path' or 'call' discriminator in v0.9/v0.9.1."
        );
    }

    private static Dictionary<string, object?> ParseAndValidatePlainObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var prop in element.EnumerateObject())
        {
            string key = prop.Name;

            // Reject single leading @
            if (key.Length >= 1 && key[0] == '@')
            {
                if (key.Length >= 2 && key[1] == '@')
                {
                    // Unescape leading @@ to @
                    key = key[1..];
                }
                else
                {
                    throw new JsonException(
                        $"Invalid dynamic object key '{prop.Name}': single '@' prefix is reserved for protocol directives."
                    );
                }
            }

            dict[key] = ConvertJsonElement(prop.Value);
        }

        return dict;
    }

    private static object? ConvertJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => el.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => ConvertJsonObjectElement(el),
            _ => el.GetRawText(),
        };
    }

    private static object? ConvertJsonObjectElement(JsonElement el)
    {
        bool isV10 = CurrentProtocolVersion.Value == A2UiProtocol.Version10;
        if (isV10)
        {
            if (el.TryGetProperty("@path", out var atPathEl))
            {
                int propCount = 0;
                foreach (var _ in el.EnumerateObject())
                {
                    propCount++;
                }

                if (propCount > 1)
                {
                    throw new JsonException("DataBinding with '@path' cannot contain additional properties.");
                }

                if (atPathEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(atPathEl.GetString()))
                {
                    throw new JsonException("DataBinding '@path' must be a non-empty string.");
                }

                return new DynamicValue.PathValue(atPathEl.GetString()!);
            }

            if (el.TryGetProperty("@call", out var atCallEl))
            {
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Name is not ("@call" or "args" or "catalogId" or "returnType"))
                    {
                        throw new JsonException(
                            $"FunctionCall with '@call' cannot contain unexpected property '{prop.Name}'."
                        );
                    }
                }

                if (atCallEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(atCallEl.GetString()))
                {
                    throw new JsonException("FunctionCall '@call' property must be a non-empty string.");
                }

                string callName = atCallEl.GetString()!;
                if (callName != "@index" && !Uax31Validator.IsValidIdentifier(callName))
                {
                    throw new JsonException(
                        $"Function name '{callName}' does not conform to UAX 31 identifier syntax."
                    );
                }

                string? catalogId = el.TryGetProperty("catalogId", out var catEl) ? catEl.GetString() : null;
                string? returnType = el.TryGetProperty("returnType", out var retEl) ? retEl.GetString() : null;

                if (returnType is not null && !s_validReturnTypes.Contains(returnType))
                {
                    throw new JsonException(
                        $"Invalid returnType '{returnType}'. Expected string, number, boolean, array, object, or any."
                    );
                }

                Dictionary<string, JsonElement>? args = null;
                if (el.TryGetProperty("args", out var argsEl))
                {
                    if (argsEl.ValueKind != JsonValueKind.Object)
                    {
                        throw new JsonException("FunctionCall args must be a JSON object.");
                    }

                    args = [];
                    foreach (var argProp in argsEl.EnumerateObject())
                    {
                        if (!Uax31Validator.IsValidIdentifier(argProp.Name))
                        {
                            throw new JsonException(
                                $"FunctionCall argument name '{argProp.Name}' does not conform to UAX 31 identifier syntax."
                            );
                        }

                        args[argProp.Name] = argProp.Value.Clone();
                    }
                }

                return new DynamicValue.FunctionValue(
                    new FunctionCallValue
                    {
                        Call = callName,
                        CatalogId = catalogId,
                        ReturnType = returnType,
                        Args = args,
                    }
                );
            }
        }

        return ParseAndValidatePlainObject(el);
    }

    private static void WriteObjectValue(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, object?> dict,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        bool isV10 = CurrentProtocolVersion.Value == A2UiProtocol.Version10;
        foreach (var (k, v) in dict)
        {
            // Escape literal @ key with prefix doubling if in v1.0 mode
            string wireKey = isV10 && k.StartsWith('@') ? "@" + k : k;
            writer.WritePropertyName(wireKey);
            WriteObjectValueChild(writer, v, options);
        }
        writer.WriteEndObject();
    }

    private static void WriteObjectValueChild(Utf8JsonWriter writer, object? v, JsonSerializerOptions options)
    {
        switch (v)
        {
            case null:
                writer.WriteNullValue();
                break;
            case DynamicValue dv:
                JsonSerializer.Serialize(writer, dv, options);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case JsonElement je:
                je.WriteTo(writer);
                break;
            case IReadOnlyDictionary<string, object?> nested:
                WriteObjectValue(writer, nested, options);
                break;
            case System.Collections.IEnumerable list when v is not string:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteObjectValueChild(writer, item, options);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(v.ToString());
                break;
        }
    }

    private sealed class VersionScope : IDisposable
    {
        private readonly string? _previousVersion;

        public VersionScope(string version)
        {
            this._previousVersion = CurrentProtocolVersion.Value;
            CurrentProtocolVersion.Value = version;
        }

        public void Dispose()
        {
            CurrentProtocolVersion.Value = this._previousVersion;
        }
    }
}
