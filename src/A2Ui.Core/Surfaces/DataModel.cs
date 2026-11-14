using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Per-surface data model store.
/// Resolves DynamicValue paths against the stored state.
/// Applies UpdateDataModel messages using JSON Pointer set/delete semantics.
/// </summary>
/// <remarks>
/// <see cref="DataModel"/> is not thread-safe. Mutations (<see cref="Apply(string?, JsonElement?, bool)"/>,
/// <see cref="SetSnapshot"/>, <see cref="Delete"/>) and reads (<see cref="Resolve(DynamicValue?)"/>,
/// <see cref="GetArrayLength"/>, <see cref="ToJson"/>, <see cref="TopLevelKeys"/>) must not race, and in UI hosts
/// must be serialized onto the owning host/UI thread (for example, Avalonia <c>Dispatcher.UIThread</c> or Blazor
/// <c>InvokeAsync</c>).
/// </remarks>
public sealed class DataModel
{
    private const int MaxArrayIndex = 10000;

    private static readonly JsonSerializerOptions s_compactOptions = new();
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    /// <summary>Cached <see cref="JsonElement"/> representing JSON <c>null</c>.</summary>
    public static JsonElement NullElement { get; } = JsonDocument.Parse("null").RootElement.Clone();

    private JsonObject _root = [];

    /// <summary>
    /// Snapshot of the top-level keys currently present in the data model root.
    /// Not thread-safe — callers must synchronize with concurrent
    /// <see cref="Apply(UpdateDataModel, bool)"/> / <see cref="SetSnapshot"/> invocations (matches the
    /// rest of <see cref="DataModel"/>: mutation and enumeration must not race).
    /// </summary>
    public IReadOnlyCollection<string> TopLevelKeys => [.. this._root.Select(kvp => kvp.Key)];

    /// <summary>
    /// Split a JSON Pointer path into unescaped segments per RFC 6901,
    /// normalizing leading, trailing, and consecutive slashes.
    /// Order matters: ~1 → / before ~0 → ~.
    /// </summary>
    private static string[] SplitPath(string path) =>
        [.. path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(UnescapeSegment)];

    /// <summary>
    /// Returns true if <paramref name="segment"/> matches RFC 6901 §4 array index grammar:
    /// "0" or a non-empty sequence of ASCII digits not starting with '0'.
    /// </summary>
    private static bool IsArrayIndexSyntax(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        if (segment.Length == 1)
        {
            return segment[0] is >= '0' and <= '9';
        }

        if (segment[0] is < '1' or > '9')
        {
            return false;
        }

        for (int i = 1; i < segment.Length; i++)
        {
            if (segment[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a valid RFC 6901 array index within <see cref="MaxArrayIndex"/>.
    /// </summary>
    private static bool TryParseArrayIndex(string segment, out int index)
    {
        if (
            IsArrayIndexSyntax(segment)
            && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed <= MaxArrayIndex
        )
        {
            index = parsed;
            return true;
        }

        index = -1;
        return false;
    }

    private static string UnescapeSegment(string segment)
    {
        int tildeIdx = segment.IndexOf('~');
        if (tildeIdx < 0)
        {
            return segment;
        }

        var sb = new System.Text.StringBuilder(segment.Length);
        for (int i = 0; i < segment.Length; i++)
        {
            if (segment[i] != '~')
            {
                sb.Append(segment[i]);
                continue;
            }

            if (i + 1 >= segment.Length)
            {
                throw new FormatException($"Invalid JSON Pointer escape: trailing '~' in segment '{segment}'.");
            }

            char next = segment[i + 1];
            switch (next)
            {
                case '0':
                    sb.Append('~');
                    break;
                case '1':
                    sb.Append('/');
                    break;
                default:
                    throw new FormatException(
                        $"Invalid JSON Pointer escape '~{next}' in segment '{segment}'. Only ~0 and ~1 are valid (RFC 6901)."
                    );
            }

            i++; // skip the character after ~
        }

        return sb.ToString();
    }

    /// <summary>Serialize the current data model state to a JSON string.</summary>
    public string ToJson(bool indented = false) => this._root.ToJsonString(indented ? s_indentedOptions : s_compactOptions);

    /// <summary>
    /// Replace the entire data model with the given JSON object.
    /// Throws <see cref="JsonException"/> if the snapshot is not a JSON object.
    /// </summary>
    public void SetSnapshot(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Cannot set data model snapshot: expected JSON object, got {snapshot.ValueKind}.");
        }

        this._root = JsonObject.Create(snapshot.Clone()) ?? [];
    }

    /// <summary>
    /// Deletes the property or array element at the specified JSON Pointer path.
    /// If path is null or "/", clears the entire data model.
    /// </summary>
    public void Delete(string? jsonPointer)
    {
        if (string.IsNullOrEmpty(jsonPointer))
        {
            this._root = [];
            return;
        }

        var segments = SplitPath(jsonPointer);
        if (segments.Length == 0)
        {
            this._root = [];
            return;
        }

        this.DeleteAtPath(segments, removeFromArray: true);
    }

    /// <summary>
    /// Apply a single UpdateDataModel message.
    /// Null or "/" path → replace entire model.
    /// Null value → delete at path.
    /// </summary>
    public void Apply(UpdateDataModel update, bool deleteOnNull = false) =>
        this.Apply(update.Path, update.Value, deleteOnNull);

    /// <summary>
    /// Apply an update at the specified JSON pointer path.
    /// When <paramref name="deleteOnNull"/> is true, a JSON null value at a non-root path deletes the key/index.
    /// </summary>
    public void Apply(string? path, JsonElement? value, bool deleteOnNull = false)
    {
        bool isNullValue = value is null || (deleteOnNull && value.Value.ValueKind == JsonValueKind.Null);

        var segments = string.IsNullOrEmpty(path) ? [] : SplitPath(path);

        // Null or root path → replace entire model or clear it
        if (segments.Length == 0)
        {
            if (value is { } val && val.ValueKind != JsonValueKind.Null)
            {
                this.SetSnapshot(val);
            }
            else
            {
                this._root = [];
            }

            return;
        }

        // Null value → delete at path
        if (isNullValue)
        {
            this.DeleteAtPath(segments, removeFromArray: value is null && deleteOnNull);
            return;
        }

        // Set value at path — supports both object keys and array indices
        JsonNode container = this._root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string seg = segments[i];
            string nextSeg = segments[i + 1];
            bool nextIsIndex = IsArrayIndexSyntax(nextSeg);

            if (
                nextIsIndex
                && !TryParseArrayIndex(nextSeg, out _)
                && !(container is JsonObject existingObj && existingObj[seg] is JsonObject)
            )
            {
                throw new JsonException(
                    $"Cannot set path: array index '{nextSeg}' exceeds maximum allowed index ({MaxArrayIndex})."
                );
            }

            container = NavigateOrCreate(container, seg, createArray: nextIsIndex);
        }

        string lastSeg = segments[^1];
        JsonNode? newValue = JsonElementToJsonNode(value!.Value);

        if (container is JsonObject obj)
        {
            obj[lastSeg] = newValue;
        }
        else if (container is JsonArray arr)
        {
            if (!IsArrayIndexSyntax(lastSeg))
            {
                throw new JsonException(
                    $"Cannot set path: non-numeric segment '{lastSeg}' is not a valid index for a JsonArray parent."
                );
            }

            if (!TryParseArrayIndex(lastSeg, out int idx))
            {
                throw new JsonException(
                    $"Cannot set path: array index '{lastSeg}' exceeds maximum allowed index ({MaxArrayIndex})."
                );
            }

            while (arr.Count <= idx)
            {
                arr.Add(null);
            }

            arr[idx] = newValue;
        }
        else
        {
            // Reject leaf writes on a non-container node.
            throw new JsonException(
                $"Cannot set path: final segment '{lastSeg}' is not a valid index for a {container.GetType().Name} parent."
            );
        }
    }

    /// <summary>
    /// Get the number of elements in the array at the given JSON Pointer path,
    /// or -1 if the path does not point to an array.
    /// </summary>
    public int GetArrayLength(string path)
    {
        JsonNode? node = this.ResolvePath(path);
        return node is JsonArray arr ? arr.Count : -1;
    }

    /// <summary>Resolve a DynamicValue. Returns null if path not found or FunctionCall.</summary>
    public string? Resolve(DynamicValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Match(
            onString: s => s.Value,
            onNumber: n => n.Value.ToString(CultureInfo.InvariantCulture),
            onBool: b => b.Value ? "true" : "false",
            onArray: a => this.ResolveArray(a, null),
            onPath: p => this.ResolvePathAsString(p.DataPath),
            onFunction: _ => null,
            onObject: o => this.ResolveObject(o.Value, null)
        );
    }

    /// <summary>
    /// Resolve a DynamicValue, scoping relative path references to <paramref name="basePath"/>.
    /// A <see cref="DynamicValue.PathValue"/> whose <c>DataPath</c> does not start with <c>/</c> is
    /// treated as relative and resolved against <c>basePath + "/" + path</c>. Absolute paths
    /// (leading <c>/</c>) and non-path values (strings, numbers, booleans, arrays, function
    /// calls) are unaffected by the scope. Pass <see langword="null"/> or an empty string to
    /// resolve at the root — identical to <see cref="Resolve(DynamicValue?)"/>.
    /// </summary>
    /// <remarks>
    /// Supports A2UI template Child Scope where children of a template instance resolve
    /// relative paths against the current iteration item.
    /// <paramref name="basePath"/> must be an absolute JSON Pointer (starting with <c>/</c>,
    /// e.g. <c>/items/0</c>) or null/empty; trailing slashes are tolerated.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="basePath"/> is non-empty and does not start with <c>/</c>.
    /// </exception>
    public string? Resolve(DynamicValue? value, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return this.Resolve(value);
        }

        if (!basePath.StartsWith('/'))
        {
            throw new ArgumentException(
                $"basePath must be an absolute JSON Pointer starting with '/', got '{basePath}'.",
                nameof(basePath)
            );
        }

        if (value is null)
        {
            return null;
        }

        return value.Match(
            onString: s => s.Value,
            onNumber: n => n.Value.ToString(CultureInfo.InvariantCulture),
            onBool: b => b.Value ? "true" : "false",
            onArray: a => this.ResolveArray(a, basePath),
            onPath: p => this.ResolvePathAsString(this.ScopePath(p.DataPath, basePath)),
            onFunction: _ => null,
            onObject: o => this.ResolveObject(o.Value, basePath)
        );
    }

    /// <summary>
    /// Resolve a <see cref="DynamicValue"/> to its native <see cref="JsonElement"/> representation
    /// (preserving strings, numbers, booleans, arrays, objects, and <c>null</c> for missing paths),
    /// scoping relative path references to <paramref name="basePath"/>.
    /// Returns <see langword="null"/> when <paramref name="value"/> is <see langword="null"/> or a function call.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="basePath"/> is non-empty and does not start with <c>/</c>.
    /// </exception>
    public JsonElement? ResolveJsonElement(DynamicValue? value, string? basePath = null)
    {
        if (!string.IsNullOrEmpty(basePath) && !basePath.StartsWith('/'))
        {
            throw new ArgumentException(
                $"basePath must be an absolute JSON Pointer starting with '/', got '{basePath}'.",
                nameof(basePath)
            );
        }

        if (value is null)
        {
            return null;
        }

        return value.Match<JsonElement?>(
            onString: s => JsonSerializer.SerializeToElement(s.Value, A2UiJsonSerializerContext.Default.String),
            onNumber: n => SerializeNumberToElement(n.Value),
            onBool: b => JsonSerializer.SerializeToElement(b.Value, A2UiJsonSerializerContext.Default.Boolean),
            onArray: a => this.ResolveArrayToElement(a, basePath),
            onPath: p => this.ResolvePathToElement(this.ScopePath(p.DataPath, basePath)),
            onFunction: _ => null,
            onObject: o => this.ResolveObjectToElement(o.Value, basePath)
        );
    }

    /// <summary>
    /// Serializes a <see cref="double"/> to a <see cref="JsonElement"/>, writing integral values as
    /// JSON integers (<see cref="long"/>) and fractional values as JSON floating-point numbers.
    /// </summary>
    public static JsonElement SerializeNumberToElement(double d)
    {
        if (double.IsFinite(d) && Math.Truncate(d) == d && d is >= long.MinValue and <= long.MaxValue)
        {
            return JsonSerializer.SerializeToElement((long)d, A2UiJsonSerializerContext.Default.Int64);
        }

        return JsonSerializer.SerializeToElement(d, A2UiJsonSerializerContext.Default.Double);
    }

    private string ResolveObject(IReadOnlyDictionary<string, object?> dict, string? basePath)
    {
        var resolved = this.ResolveObjectValue(dict, basePath);
        return SerializeResolvedGraph(resolved);
    }

    private JsonElement ResolveObjectToElement(IReadOnlyDictionary<string, object?> dict, string? basePath)
    {
        var resolved = this.ResolveObjectValue(dict, basePath);
        return SerializeResolvedGraphToElement(resolved);
    }

    private object? ResolveObjectValue(object? val, string? basePath)
    {
        return val switch
        {
            null => null,
            DynamicValue.PathValue p => this.ResolvePathToElement(this.ScopePath(p.DataPath, basePath)),
            DynamicValue dv => this.ResolveJsonElement(dv, basePath),
            IReadOnlyDictionary<string, object?> d => d.ToDictionary(
                kvp => kvp.Key,
                kvp => this.ResolveObjectValue(kvp.Value, basePath)
            ),
            IEnumerable<object?> arr => arr.Select(item => this.ResolveObjectValue(item, basePath)).ToList(),
            _ => val,
        };
    }

    private string ResolveArray(DynamicValue.ArrayValue a, string? basePath)
    {
        List<object?> list = this.ResolveArrayItems(a, basePath);
        return SerializeResolvedGraph(list);
    }

    private JsonElement ResolveArrayToElement(DynamicValue.ArrayValue a, string? basePath)
    {
        List<object?> list = this.ResolveArrayItems(a, basePath);
        return SerializeResolvedGraphToElement(list);
    }

    private List<object?> ResolveArrayItems(DynamicValue.ArrayValue a, string? basePath)
    {
        var list = new List<object?>();
        foreach (var el in a.Value.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("@path", out var pEl) && pEl.ValueKind == JsonValueKind.String)
            {
                list.Add(this.ResolvePathToElement(this.ScopePath(pEl.GetString()!, basePath)));
            }
            else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("path", out var v09PathEl) && v09PathEl.ValueKind == JsonValueKind.String && el.EnumerateObject().Count() == 1)
            {
                list.Add(this.ResolvePathToElement(this.ScopePath(v09PathEl.GetString()!, basePath)));
            }
            else
            {
                list.Add(ConvertJsonElement(el));
            }
        }

        return list;
    }

    private static string SerializeResolvedGraph(object? graph)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteResolvedValue(writer, graph);
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static JsonElement SerializeResolvedGraphToElement(object? graph)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteResolvedValue(writer, graph);
        }

        using var doc = JsonDocument.Parse(stream.GetBuffer().AsMemory(0, (int)stream.Length));
        return doc.RootElement.Clone();
    }

    internal static void WriteResolvedValue(Utf8JsonWriter writer, object? val)
    {
        switch (val)
        {
            case IReadOnlyDictionary<string, object?> dict:
                writer.WriteStartObject();
                foreach (var (k, v) in dict)
                {
                    writer.WritePropertyName(k);
                    WriteResolvedValue(writer, v);
                }

                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable seq when val is not string:
                writer.WriteStartArray();
                foreach (var item in seq)
                {
                    WriteResolvedValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                WritePrimitiveOrElementValue(writer, val);
                break;
        }
    }

    internal static void WritePrimitiveOrElementValue(Utf8JsonWriter writer, object? val)
    {
        switch (val)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case JsonElement je:
                je.WriteTo(writer);
                break;
            default:
                writer.WriteStringValue(val.ToString());
                break;
        }
    }

    private string ScopePath(string path, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath) || path.StartsWith('/'))
        {
            return path;
        }

        return basePath.TrimEnd('/') + "/" + path;
    }

    private static object? ConvertJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => el.GetRawText(),
        };
    }

    private string? ResolvePathAsString(string path)
    {
        JsonNode? node = this.ResolvePath(path);
        return NodeToString(node);
    }

    private JsonElement ResolvePathToElement(string path)
    {
        JsonNode? node = this.ResolvePath(path);
        if (node is null)
        {
            return NullElement;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            node.WriteTo(writer);
        }

        using var doc = JsonDocument.Parse(stream.GetBuffer().AsMemory(0, (int)stream.Length));
        return doc.RootElement.Clone();
    }

    private JsonNode? ResolvePath(string path)
    {
        var segments = SplitPath(path);
        JsonNode? node = this._root;

        foreach (var seg in segments)
        {
            if (node is JsonObject obj && obj.ContainsKey(seg))
            {
                node = obj[seg];
            }
            else if (node is JsonArray arr && TryParseArrayIndex(seg, out int idx) && idx < arr.Count)
            {
                node = arr[idx];
            }
            else
            {
                return null;
            }
        }

        return node;
    }

    private static JsonNode NavigateOrCreate(JsonNode parent, string segment, bool createArray)
    {
        if (parent is JsonObject obj)
        {
            // Preserve existing container regardless of createArray hint
            if (obj[segment] is JsonObject or JsonArray)
            {
                return obj[segment]!;
            }

            // Reject traversing through a primitive value.
            if (obj[segment] is JsonValue)
            {
                throw new JsonException(
                    $"Cannot set path: cannot traverse path segment '{segment}' because value at this key is a primitive, not an object or array."
                );
            }

            // No container at this key — create based on hint
            JsonNode child = createArray ? new JsonArray() : new JsonObject();
            obj[segment] = child;
            return child;
        }

        if (parent is JsonArray arr)
        {
            if (!IsArrayIndexSyntax(segment))
            {
                throw new JsonException(
                    $"Cannot set path: non-numeric segment '{segment}' is not a valid index for a JsonArray parent."
                );
            }

            if (!TryParseArrayIndex(segment, out int idx))
            {
                throw new JsonException(
                    $"Cannot set path: array index '{segment}' exceeds maximum allowed index ({MaxArrayIndex})."
                );
            }

            while (arr.Count <= idx)
            {
                arr.Add(null);
            }

            if (arr[idx] is JsonObject or JsonArray)
            {
                return arr[idx]!;
            }

            // Reject traversing through a primitive value.
            if (arr[idx] is JsonValue)
            {
                throw new JsonException(
                    $"Cannot set path: cannot traverse array index '{segment}' because value at this index is a primitive, not an object or array."
                );
            }

            JsonNode child = createArray ? new JsonArray() : new JsonObject();
            arr[idx] = child;
            return child;
        }

        throw new JsonException(
            $"Cannot set path: cannot navigate segment '{segment}' on a {parent.GetType().Name} node. "
                + "Expected JsonObject or JsonArray as parent."
        );
    }

    private void DeleteAtPath(string[] segments, bool removeFromArray = false)
    {
        if (segments.Length == 1)
        {
            this._root.Remove(segments[0]);
            return;
        }

        // Navigate to parent of the target — supports both object and array nodes
        JsonNode current = this._root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string seg = segments[i];
            if (current is JsonObject obj && obj.ContainsKey(seg))
            {
                current = obj[seg]!;
            }
            else if (
                current is JsonArray arr
                && TryParseArrayIndex(seg, out int idx)
                && idx < arr.Count
                && arr[idx] is not null
            )
            {
                current = arr[idx]!;
            }
            else
            {
                return; // path doesn't exist, nothing to delete
            }
        }

        // Remove the final segment from its parent
        string lastSeg = segments[^1];
        if (current is JsonObject parentObj)
        {
            parentObj.Remove(lastSeg);
        }
        else if (
            current is JsonArray parentArr
            && TryParseArrayIndex(lastSeg, out int lastIdx)
            && lastIdx < parentArr.Count
        )
        {
            if (removeFromArray)
            {
                parentArr.RemoveAt(lastIdx);
            }
            else
            {
                parentArr[lastIdx] = null;
            }
        }
    }

    private static JsonNode? JsonElementToJsonNode(JsonElement element)
    {
        JsonElement cloned = element.Clone();
        return cloned.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Object => JsonObject.Create(cloned),
            JsonValueKind.Array => JsonArray.Create(cloned),
            _ => JsonValue.Create(cloned),
        };
    }

    private static string? NodeToString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue val)
        {
            if (val.TryGetValue<string>(out var s))
            {
                return s;
            }

            if (val.TryGetValue<double>(out var d))
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }

            if (val.TryGetValue<bool>(out var b))
            {
                return b ? "true" : "false";
            }

            if (val.TryGetValue<int>(out var i))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (val.TryGetValue<long>(out var l))
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }
        }

        // Objects and arrays: JSON stringify
        return node.ToJsonString();
    }
}
