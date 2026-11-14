using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// A2UI v1.0 renderer-to-agent message envelope.
/// Contains exactly one of: action, callAgentFunction, rendererFunctionResponse, or error.
/// Maps to specification/v1_0/json/renderer_to_agent.json.
/// </summary>
[JsonConverter(typeof(RendererToAgentMessageConverter))]
public sealed record RendererToAgentMessage
{
    /// <summary>Protocol version string (must be "1.0").</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = A2UiProtocol.Version10;

    /// <summary>User action payload.</summary>
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RendererAction? Action { get; init; }

    /// <summary>Remote agent function execution request.</summary>
    [JsonPropertyName("callAgentFunction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CallAgentFunction? CallAgentFunction { get; init; }

    /// <summary>Renderer response to an agent-initiated function execution.</summary>
    [JsonPropertyName("rendererFunctionResponse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FunctionResponse? RendererFunctionResponse { get; init; }

    /// <summary>Renderer-side error payload.</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RendererError? Error { get; init; }

    /// <summary>
    /// Validate that this message conforms to the A2UI v1.0 renderer envelope constraints:
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
            (this.Action is not null ? 1 : 0)
            + (this.CallAgentFunction is not null ? 1 : 0)
            + (this.RendererFunctionResponse is not null ? 1 : 0)
            + (this.Error is not null ? 1 : 0);

        if (opCount == 0)
        {
            violations.Add(
                "Message must contain exactly one operation (action, callAgentFunction, rendererFunctionResponse, or error)"
            );
        }
        else if (opCount > 1)
        {
            violations.Add($"Message contains {opCount} operations; exactly one is allowed");
        }

        this.Action?.Validate();
        this.CallAgentFunction?.Validate();
        this.RendererFunctionResponse?.Validate();
        this.Error?.Validate();

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}

internal sealed class RendererToAgentMessageConverter : JsonConverter<RendererToAgentMessage>
{
    public override RendererToAgentMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

        RendererAction? action = null;
        if (root.TryGetProperty("action", out var actEl))
        {
            action = actEl.Deserialize<RendererAction>(options);
        }

        CallAgentFunction? caf = null;
        if (root.TryGetProperty("callAgentFunction", out var cafEl))
        {
            caf = cafEl.Deserialize<CallAgentFunction>(options);
        }

        FunctionResponse? rfr = null;
        if (root.TryGetProperty("rendererFunctionResponse", out var rfrEl))
        {
            rfr = rfrEl.Deserialize<FunctionResponse>(options);
        }

        RendererError? error = null;
        if (root.TryGetProperty("error", out var errEl))
        {
            error = errEl.Deserialize<RendererError>(options);
        }

        return new RendererToAgentMessage
        {
            Version = version,
            Action = action,
            CallAgentFunction = caf,
            RendererFunctionResponse = rfr,
            Error = error,
        };
    }

    public override void Write(Utf8JsonWriter writer, RendererToAgentMessage value, JsonSerializerOptions options)
    {
        using IDisposable? scope =
            A2UiJsonUtilities.GetProtocolVersion(options) is null
                ? DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10)
                : null;
        writer.WriteStartObject();
        writer.WriteString("version", value.Version);
        if (value.Action is not null)
        {
            writer.WritePropertyName("action");
            JsonSerializer.Serialize(writer, value.Action, options);
        }
        if (value.CallAgentFunction is not null)
        {
            writer.WritePropertyName("callAgentFunction");
            JsonSerializer.Serialize(writer, value.CallAgentFunction, options);
        }
        if (value.RendererFunctionResponse is not null)
        {
            writer.WritePropertyName("rendererFunctionResponse");
            JsonSerializer.Serialize(writer, value.RendererFunctionResponse, options);
        }
        if (value.Error is not null)
        {
            writer.WritePropertyName("error");
            JsonSerializer.Serialize(writer, value.Error, options);
        }
        writer.WriteEndObject();
    }
}
