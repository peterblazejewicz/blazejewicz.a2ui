using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// A2UI v1.0 agent-to-renderer message — one JSONL line on the wire.
/// Each message contains version "1.0" and exactly one top-level operation key.
/// Maps to specification/v1_0/json/agent_to_renderer.json.
/// </summary>
[JsonConverter(typeof(AgentToRendererMessageConverter))]
public sealed record AgentToRendererMessage
{
    /// <summary>Protocol version string (must be "1.0").</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = A2UiProtocol.Version10;

    /// <summary>Create a new surface with optional inline components and data model.</summary>
    [JsonPropertyName("createSurface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CreateSurface? CreateSurface { get; init; }

    /// <summary>Update the component tree on an existing surface.</summary>
    [JsonPropertyName("updateComponents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UpdateComponents? UpdateComponents { get; init; }

    /// <summary>Update the data model on an existing surface.</summary>
    [JsonPropertyName("updateDataModel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UpdateDataModel? UpdateDataModel { get; init; }

    /// <summary>Delete an existing surface by its identifier.</summary>
    [JsonPropertyName("deleteSurface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeleteSurface? DeleteSurface { get; init; }

    /// <summary>Execute a local renderer function on behalf of the agent.</summary>
    [JsonPropertyName("callRendererFunction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CallRendererFunction? CallRendererFunction { get; init; }

    /// <summary>Agent response to a renderer-initiated function execution.</summary>
    [JsonPropertyName("agentFunctionResponse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FunctionResponse? AgentFunctionResponse { get; init; }

    /// <summary>
    /// Validate that this message conforms to the A2UI v1.0 envelope constraints:
    /// version must be "1.0" and exactly one operation must be present.
    /// </summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when constraints are violated.</exception>
    public void Validate()
    {
        var violations = new List<string>();

        if (this.Version is null)
        {
            violations.Add("Missing required 'version' property");
        }
        else if (this.Version != A2UiProtocol.Version10)
        {
            violations.Add($"Unsupported version '{this.Version}'; expected '{A2UiProtocol.Version10}'");
        }

        int opCount =
            (this.CreateSurface is not null ? 1 : 0)
            + (this.UpdateComponents is not null ? 1 : 0)
            + (this.UpdateDataModel is not null ? 1 : 0)
            + (this.DeleteSurface is not null ? 1 : 0)
            + (this.CallRendererFunction is not null ? 1 : 0)
            + (this.AgentFunctionResponse is not null ? 1 : 0);

        if (opCount == 0)
        {
            violations.Add(
                "Message must contain exactly one operation (createSurface, updateComponents, updateDataModel, deleteSurface, callRendererFunction, or agentFunctionResponse)"
            );
        }
        else if (opCount > 1)
        {
            violations.Add($"Message contains {opCount} operations; exactly one is allowed");
        }

        this.CreateSurface?.Validate();
        this.UpdateComponents?.Validate();
        this.UpdateDataModel?.Validate();
        this.CallRendererFunction?.Validate();
        this.AgentFunctionResponse?.Validate();

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}

internal sealed class AgentToRendererMessageConverter : JsonConverter<AgentToRendererMessage>
{
    public override AgentToRendererMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using IDisposable? scope =
            A2UiJsonUtilities.GetProtocolVersion(options) is null
                ? DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10)
                : null;
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        string version = root.TryGetProperty("version", out var vEl) && vEl.ValueKind == JsonValueKind.String
            ? (vEl.GetString() ?? A2UiProtocol.Version10)
            : A2UiProtocol.Version10;

        CreateSurface? cs = null;
        if (root.TryGetProperty("createSurface", out var csEl))
        {
            cs = csEl.Deserialize<CreateSurface>(options);
        }

        UpdateComponents? uc = null;
        if (root.TryGetProperty("updateComponents", out var ucEl))
        {
            uc = ucEl.Deserialize<UpdateComponents>(options);
        }

        UpdateDataModel? ud = null;
        if (root.TryGetProperty("updateDataModel", out var udEl))
        {
            ud = udEl.Deserialize<UpdateDataModel>(options);
        }

        DeleteSurface? ds = null;
        if (root.TryGetProperty("deleteSurface", out var dsEl))
        {
            ds = dsEl.Deserialize<DeleteSurface>(options);
        }

        CallRendererFunction? crf = null;
        if (root.TryGetProperty("callRendererFunction", out var crfEl))
        {
            crf = crfEl.Deserialize<CallRendererFunction>(options);
        }

        FunctionResponse? afr = null;
        if (root.TryGetProperty("agentFunctionResponse", out var afrEl))
        {
            afr = afrEl.Deserialize<FunctionResponse>(options);
        }

        return new AgentToRendererMessage
        {
            Version = version,
            CreateSurface = cs,
            UpdateComponents = uc,
            UpdateDataModel = ud,
            DeleteSurface = ds,
            CallRendererFunction = crf,
            AgentFunctionResponse = afr,
        };
    }

    public override void Write(Utf8JsonWriter writer, AgentToRendererMessage value, JsonSerializerOptions options)
    {
        using IDisposable? scope =
            A2UiJsonUtilities.GetProtocolVersion(options) is null
                ? DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10)
                : null;
        writer.WriteStartObject();
        writer.WriteString("version", value.Version);
        if (value.CreateSurface is not null)
        {
            writer.WritePropertyName("createSurface");
            JsonSerializer.Serialize(writer, value.CreateSurface, options);
        }
        if (value.UpdateComponents is not null)
        {
            writer.WritePropertyName("updateComponents");
            JsonSerializer.Serialize(writer, value.UpdateComponents, options);
        }
        if (value.UpdateDataModel is not null)
        {
            writer.WritePropertyName("updateDataModel");
            JsonSerializer.Serialize(writer, value.UpdateDataModel, options);
        }
        if (value.DeleteSurface is not null)
        {
            writer.WritePropertyName("deleteSurface");
            JsonSerializer.Serialize(writer, value.DeleteSurface, options);
        }
        if (value.CallRendererFunction is not null)
        {
            writer.WritePropertyName("callRendererFunction");
            JsonSerializer.Serialize(writer, value.CallRendererFunction, options);
        }
        if (value.AgentFunctionResponse is not null)
        {
            writer.WritePropertyName("agentFunctionResponse");
            JsonSerializer.Serialize(writer, value.AgentFunctionResponse, options);
        }
        writer.WriteEndObject();
    }
}
