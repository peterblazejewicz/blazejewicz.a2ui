using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Surfaces;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Bindings;

/// <summary>
/// JSON converter for <see cref="DynamicValue"/>.
/// Supports v0.9 (path, call) and v1.0 (@path, @call, @@ unescaping, single-@ rejection, plain object literals).
/// </summary>
/// <param name="fixedProtocolVersion">Optional fixed protocol version overriding options and ambient context.</param>
public sealed class DynamicValueConverter(string? fixedProtocolVersion) : JsonConverter<DynamicValue>
{
    /// <summary>Current protocol version active for deserialization context.</summary>
    internal static readonly AsyncLocal<string?> CurrentProtocolVersion = new();

    private static readonly ExpressionParser s_expressionParser = new();

    private static readonly FrozenSet<string> s_validReturnTypes = FrozenSet.ToFrozenSet(
        [
            "string",
            "number",
            "boolean",
            "array",
            "object",
            "any",
        ],
        StringComparer.Ordinal
    );

    private readonly string? _fixedProtocolVersion = fixedProtocolVersion;

    /// <summary>Initializes a new instance of the <see cref="DynamicValueConverter"/> class.</summary>
    public DynamicValueConverter()
        : this(null) { }

    /// <summary>Enters a scoped protocol version context for the duration of the returned disposable.</summary>
    public static IDisposable EnterVersionScope(string version) => new VersionScope(version);

    internal static string? ResolveProtocolVersion(string? fixedProtocolVersion, JsonSerializerOptions? options) =>
        fixedProtocolVersion ?? A2UiJsonUtilities.GetProtocolVersion(options) ?? CurrentProtocolVersion.Value;

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
            JsonTokenType.StartObject => this.ReadObject(ref reader, options),
            _ => throw new JsonException(
                $"DynamicValue cannot be read from JSON token '{reader.TokenType}'. Expected string, number, boolean, array, or object."
            ),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DynamicValue value, JsonSerializerOptions options)
    {
        string? activeVersion = ResolveProtocolVersion(this._fixedProtocolVersion, options);
        bool isV10 = activeVersion == A2UiProtocol.Version10;

        value.Switch(
            onString: s => writer.WriteStringValue(s.Value),
            onNumber: n => writer.WriteNumberValue(n.Value),
            onBool: b => writer.WriteBooleanValue(b.Value),
            onArray: a => a.Value.WriteTo(writer),
            onPath: p =>
            {
                writer.WriteStartObject();
                writer.WriteString(isV10 ? "@path" : "path", p.DataPath);
                writer.WriteEndObject();
            },
            onFunction: f =>
            {
                using IDisposable? scope =
                    this._fixedProtocolVersion is not null && A2UiJsonUtilities.GetProtocolVersion(options) is null
                        ? EnterVersionScope(this._fixedProtocolVersion)
                        : null;
                JsonSerializer.Serialize(writer, f.Call, options);
            },
            onObject: o => this.WriteObjectValue(writer, o.Value, options, isV10)
        );
    }

    private DynamicValue ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        bool isV10 = ResolveProtocolVersion(this._fixedProtocolVersion, options) == A2UiProtocol.Version10;

        if (isV10)
        {
            if (TryParseV10Directive(root, out DynamicValue? directive))
            {
                return directive;
            }

            // Plain object literal in v1.0
            var dict = ParseAndValidatePlainObject(root, isV10: true);
            return new DynamicValue.ObjectValue(dict);
        }

        // v0.9 / v0.9.1 mode
        if (root.TryGetProperty("@path", out _) || root.TryGetProperty("@call", out _))
        {
            throw new JsonException("Directives '@path' and '@call' are not allowed in v0.9/v0.9.1 protocol payloads.");
        }

        if (root.TryGetProperty("path", out var pathEl))
        {
            return ParseNonEmptyPath(pathEl, "path");
        }

        if (root.TryGetProperty("call", out _))
        {
            using IDisposable? scope =
                this._fixedProtocolVersion is not null && A2UiJsonUtilities.GetProtocolVersion(options) is null
                    ? EnterVersionScope(this._fixedProtocolVersion)
                    : null;
            var fc = root.Deserialize<FunctionCallValue>(options);
            return new DynamicValue.FunctionValue(fc!);
        }

        if (root.TryGetProperty("svgPath", out _))
        {
            var dict = ParseAndValidatePlainObject(root, isV10: false);
            return new DynamicValue.ObjectValue(dict);
        }

        throw new JsonException(
            "Invalid DynamicValue object: expected 'path' or 'call' discriminator in v0.9/v0.9.1."
        );
    }

    private static bool TryParseV10Directive(JsonElement element, [NotNullWhen(true)] out DynamicValue? directive)
    {
        if (element.TryGetProperty("@path", out var atPathEl))
        {
            directive = ParseAtPathValue(element, atPathEl);
            return true;
        }

        if (element.TryGetProperty("@call", out var atCallEl))
        {
            directive = ParseAtCallValue(element, atCallEl);
            return true;
        }

        directive = null;
        return false;
    }

    private static DynamicValue.PathValue ParseAtPathValue(JsonElement element, JsonElement atPathEl)
    {
        int propCount = 0;
        foreach (var _ in element.EnumerateObject())
        {
            propCount++;
        }

        if (propCount > 1)
        {
            throw new JsonException("DataBinding with '@path' cannot contain additional properties.");
        }

        return ParseNonEmptyPath(atPathEl, "@path");
    }

    private static DynamicValue.PathValue ParseNonEmptyPath(JsonElement pathEl, string propertyName)
    {
        if (pathEl.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(pathEl.GetString()))
        {
            throw new JsonException($"DataBinding '{propertyName}' must be a non-empty string.");
        }

        return new DynamicValue.PathValue(pathEl.GetString()!);
    }

    private static DynamicValue.FunctionValue ParseAtCallValue(JsonElement element, JsonElement atCallEl)
    {
        foreach (var prop in element.EnumerateObject())
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

        string? catalogId = element.TryGetProperty("catalogId", out var catEl) ? catEl.GetString() : null;
        string? returnType = element.TryGetProperty("returnType", out var retEl) ? retEl.GetString() : null;

        if (returnType is not null && !s_validReturnTypes.Contains(returnType))
        {
            throw new JsonException(
                $"Invalid returnType '{returnType}'. Expected string, number, boolean, array, object, or any."
            );
        }

        Dictionary<string, JsonElement>? args = null;
        if (element.TryGetProperty("args", out var argsEl))
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

            ValidateBasicFunctionArgs(callName, args);
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

    private static void ValidateBasicFunctionArgs(string callName, Dictionary<string, JsonElement> args)
    {
        if (
            callName == "formatString"
            && args.TryGetValue("value", out JsonElement fmtValEl)
            && fmtValEl.ValueKind == JsonValueKind.String
        )
        {
            try
            {
                s_expressionParser.Parse(fmtValEl.GetString()!);
            }
            catch (A2UiExpressionException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        }
        else if (
            callName is "formatNumber" or "formatCurrency"
            && args.TryGetValue("value", out JsonElement numValEl)
            && numValEl.ValueKind == JsonValueKind.String
            && !double.TryParse(
                numValEl.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _
            )
        )
        {
            throw new JsonException(
                $"Function '{callName}' argument 'value' must be a number, DataBinding, or FunctionCall."
            );
        }
    }

    private static Dictionary<string, object?> ParseAndValidatePlainObject(JsonElement element, bool isV10)
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

            dict[key] = ConvertJsonElement(prop.Value, isV10);
        }

        return dict;
    }

    private static object? ConvertJsonElement(JsonElement el, bool isV10)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => el.EnumerateArray().Select(item => ConvertJsonElement(item, isV10)).ToList(),
            JsonValueKind.Object => ConvertJsonObjectElement(el, isV10),
            _ => el.GetRawText(),
        };
    }

    private static object? ConvertJsonObjectElement(JsonElement el, bool isV10)
    {
        if (isV10 && TryParseV10Directive(el, out DynamicValue? directive))
        {
            return directive;
        }

        return ParseAndValidatePlainObject(el, isV10);
    }

    private void WriteObjectValue(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, object?> dict,
        JsonSerializerOptions options,
        bool isV10
    )
    {
        writer.WriteStartObject();
        foreach (var (k, v) in dict)
        {
            // Escape literal @ key with prefix doubling if in v1.0 mode
            string wireKey = isV10 && k.StartsWith('@') ? "@" + k : k;
            writer.WritePropertyName(wireKey);
            this.WriteObjectValueChild(writer, v, options, isV10);
        }
        writer.WriteEndObject();
    }

    private void WriteObjectValueChild(
        Utf8JsonWriter writer,
        object? v,
        JsonSerializerOptions options,
        bool isV10
    )
    {
        switch (v)
        {
            case DynamicValue dv:
                this.Write(writer, dv, options);
                break;
            case IReadOnlyDictionary<string, object?> nested:
                this.WriteObjectValue(writer, nested, options, isV10);
                break;
            case System.Collections.IEnumerable list when v is not string:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    this.WriteObjectValueChild(writer, item, options, isV10);
                }
                writer.WriteEndArray();
                break;
            default:
                DataModel.WritePrimitiveOrElementValue(writer, v);
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
