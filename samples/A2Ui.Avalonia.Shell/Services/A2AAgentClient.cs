using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A.V0_3;
using Blazejewicz.A2Ui.Avalonia.Shell.Models;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// A2A HTTP client that communicates with an agent supporting A2UI.
/// </summary>
public sealed class A2AAgentClient(
    HttpClient http,
    AgentConfig config,
    ILogger<A2AAgentClient> logger,
    SurfaceManager? surfaceManager = null
) : IA2AClient
{
    /// <summary>A2A extension negotiation header name. Used by DI at client registration time.</summary>
    internal const string A2UiExtensionHeader = "X-A2A-Extensions";

    /// <summary>A2UI extension URI negotiation header value (v1.0 preferred, v0.9.1, v0.9 fallback).</summary>
    internal const string A2UiExtensionUri =
        $"{A2UiProtocol.A2AExtensionUriV10}, {A2UiProtocol.A2AExtensionUriV091}, {A2UiProtocol.A2AExtensionUriV09}";

    private static readonly JsonSerializerOptions s_camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
    };

    private static readonly JsonElement s_v10Capabilities = JsonDocument
        .Parse(
            """
            {
              "1.0": {
                "supportedCatalogIds": [
                  "https://a2ui.org/catalogs/basic/v1/catalog.json",
                  "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json"
                ]
              }
            }
            """
        )
        .RootElement.Clone();

    private static readonly JsonElement s_v09Capabilities = JsonDocument
        .Parse(
            """
            {
              "v0.9": {
                "supportedCatalogIds": [
                  "https://a2ui.org/specification/v0_9/standard_catalog.json",
                  "https://a2ui.org/specification/v0_9/basic_catalog.json"
                ]
              }
            }
            """
        )
        .RootElement.Clone();

    private readonly HttpClient _http = http;
    private readonly AgentConfig _config = config;
    private readonly ILogger<A2AAgentClient> _logger = logger;
    private readonly SurfaceManager? _surfaceManager = surfaceManager;

    /// <summary>Gets the active A2A conversation context identifier persisted across turns.</summary>
    public string? ContextId { get; private set; }

    public async Task<string> GetAgentNameAsync(CancellationToken ct = default)
    {
        string cardUrl = $"{this._config.ServerUrl}/.well-known/agent-card.json";
        A2AAgentClientLog.FetchingAgentCard(this._logger, cardUrl);

        AgentCard? card = await this
            ._http.GetFromJsonAsync<AgentCard>(cardUrl, A2AJsonUtilities.DefaultOptions, ct)
            .ConfigureAwait(false);

        return !string.IsNullOrEmpty(card?.Name) ? card.Name : "Unknown Agent";
    }

    public async Task<IReadOnlyList<A2UiMessage>> SendTextAsync(string text, CancellationToken ct = default)
    {
        A2AAgentClientLog.SendingTextQuery(this._logger, Truncate(text, 200));

        List<Part> parts =
        [
            new TextPart { Text = text },
        ];

        var (task, correlationId) = await this.SendInternalAsync(parts, ct).ConfigureAwait(false);
        return this.ExtractA2UiMessages(task, correlationId);
    }

    public async Task<IReadOnlyList<string>> SendTextRawAsync(string text, CancellationToken ct = default)
    {
        A2AAgentClientLog.SendingTextQuery(this._logger, Truncate(text, 200));

        List<Part> parts =
        [
            new TextPart { Text = text },
        ];

        var (task, correlationId) = await this.SendInternalAsync(parts, ct).ConfigureAwait(false);
        return this.ExtractRawA2UiMessages(task, correlationId);
    }

    public async Task<IReadOnlyList<A2UiMessage>> SendActionAsync(object userAction, CancellationToken ct = default)
    {
        A2AAgentClientLog.SendingAction(this._logger);

        JsonElement data = JsonSerializer.SerializeToElement(userAction, s_camelCase);
        Dictionary<string, JsonElement> dataDict =
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data, s_camelCase) ?? [];

        List<Part> parts =
        [
            new DataPart
            {
                Data = dataDict,
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["mimeType"] = JsonSerializer.SerializeToElement(A2UiProtocol.MimeType),
                },
            },
        ];

        var (task, correlationId) = await this.SendInternalAsync(parts, ct).ConfigureAwait(false);
        return this.ExtractA2UiMessages(task, correlationId);
    }

    public async Task<IReadOnlyList<string>> SendActionRawAsync(object userAction, CancellationToken ct = default)
    {
        A2AAgentClientLog.SendingAction(this._logger);

        JsonElement data = JsonSerializer.SerializeToElement(userAction, s_camelCase);
        Dictionary<string, JsonElement> dataDict =
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data, s_camelCase) ?? [];

        List<Part> parts =
        [
            new DataPart
            {
                Data = dataDict,
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["mimeType"] = JsonSerializer.SerializeToElement(A2UiProtocol.MimeType),
                },
            },
        ];

        var (task, correlationId) = await this.SendInternalAsync(parts, ct).ConfigureAwait(false);
        return this.ExtractRawA2UiMessages(task, correlationId);
    }

    private async Task<(AgentTask? task, string correlationId)> SendInternalAsync(List<Part> parts, CancellationToken ct)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        string messageId = Guid.NewGuid().ToString();

        using Activity? activity = Diagnostics.Source.StartActivity("A2A.SendMessage", ActivityKind.Client);
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("http.url", this._config.ServerUrl);
        activity?.SetTag("a2a.correlation_id", correlationId);
        activity?.SetTag("a2a.message_id", messageId);

        using IDisposable? logScope = this._logger.BeginScope(
            new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["MessageId"] = messageId }
        );

        var sendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = messageId,
                ContextId = this.ContextId,
                Parts = parts,
                Metadata = this.BuildMessageMetadata(),
            },
        };

        var request = new JsonRpcRequest
        {
            Id = new JsonRpcId(correlationId),
            Method = A2AMethods.MessageSend,
            Params = JsonSerializer.SerializeToElement(sendParams, A2AJsonUtilities.DefaultOptions),
        };

        // Serialize once to get the byte count for telemetry, then POST the same bytes.
        byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, A2AJsonUtilities.DefaultOptions);
        A2AAgentClientLog.SendMessageStarted(
            this._logger,
            this._config.ServerUrl,
            requestBytes.Length,
            messageId,
            correlationId
        );

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = new ByteArrayContent(requestBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this
                ._http.PostAsync(this._config.ServerUrl, content, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            byte[] responseBodyBytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            JsonRpcResponse? rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse>(
                responseBodyBytes,
                A2AJsonUtilities.DefaultOptions
            );

            // JSON-RPC errors arrive with HTTP 200; surface them instead of silently returning no messages.
            if (rpcResponse?.Error is { } rpcError)
            {
                A2AAgentClientLog.AgentReturnedError(this._logger, rpcError.Message ?? "(no message)", correlationId);
                throw new InvalidOperationException($"Agent returned JSON-RPC error: {rpcError.Message}");
            }

            AgentTask? task = rpcResponse?.Result is { } resultNode
                ? resultNode.Deserialize<AgentTask>(A2AJsonUtilities.DefaultOptions)
                : null;

            if (!string.IsNullOrEmpty(task?.ContextId))
            {
                this.ContextId = task.ContextId;
            }

            stopwatch.Stop();

            activity?.SetTag("http.status_code", (int)response.StatusCode);
            activity?.SetTag("http.response_content_length", responseBodyBytes.LongLength);

            return (task, correlationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            A2AAgentClientLog.SendMessageFailed(
                this._logger,
                this._config.ServerUrl,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                correlationId,
                ex
            );
            throw;
        }
    }

    private List<A2UiMessage> ExtractA2UiMessages(AgentTask? task, string correlationId)
    {
        var messages = new List<A2UiMessage>();

        List<Part>? parts = task?.Status.Message?.Parts;
        if (parts is null)
        {
            A2AAgentClientLog.ResponseHasNoParts(this._logger, correlationId);
            return messages;
        }

        int messageIndex = 0;
        foreach (Part part in parts)
        {
            int currentIndex = messageIndex++;

            if (part is not DataPart { Data: { Count: > 0 } dataDict } dataPart)
            {
                continue;
            }

            // Check mimeType in metadata
            string? mime = GetMimeFromMetadata(dataPart.Metadata);
            if (mime is not null && !A2UiProtocol.IsSupportedMimeType(mime))
            {
                continue;
            }

            try
            {
                JsonElement dataElement = JsonSerializer.SerializeToElement(dataDict, A2AJsonUtilities.DefaultOptions);
                A2UiMessage? msg = dataElement.Deserialize<A2UiMessage>(s_camelCase);
                if (msg is not null)
                {
                    messages.Add(msg);
                }
            }
            catch (JsonException ex)
            {
                A2AAgentClientLog.DataPartDeserializeFailed(this._logger, currentIndex, correlationId, ex);
            }
        }

        A2AAgentClientLog.ExtractedMessages(this._logger, messages.Count, correlationId);
        return messages;
    }

    private List<string> ExtractRawA2UiMessages(AgentTask? task, string correlationId)
    {
        var messages = new List<string>();

        List<Part>? parts = task?.Status.Message?.Parts;
        if (parts is null)
        {
            A2AAgentClientLog.ResponseHasNoParts(this._logger, correlationId);
            return messages;
        }

        foreach (Part part in parts)
        {
            if (part is not DataPart { Data: { Count: > 0 } dataDict } dataPart)
            {
                continue;
            }

            // Check mimeType in metadata
            string? mime = GetMimeFromMetadata(dataPart.Metadata);
            if (mime is not null && !A2UiProtocol.IsSupportedMimeType(mime))
            {
                continue;
            }

            messages.Add(JsonSerializer.Serialize(dataDict, A2AJsonUtilities.DefaultOptions));
        }

        A2AAgentClientLog.ExtractedMessages(this._logger, messages.Count, correlationId);
        return messages;
    }

    private Dictionary<string, JsonElement> BuildMessageMetadata()
    {
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["a2uiRendererCapabilities"] = s_v10Capabilities,
            ["a2uiClientCapabilities"] = s_v09Capabilities,
        };

        if (this._surfaceManager is not null)
        {
            var v10Surfaces = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var v09Surfaces = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (Surface surface in this._surfaceManager.GetSurfaces())
            {
                if (!surface.SendDataModel)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(surface.DataModel.ToJson());
                if (surface.ProtocolVersion == A2UiProtocol.Version10)
                {
                    v10Surfaces[surface.SurfaceId] = doc.RootElement.Clone();
                }
                else
                {
                    v09Surfaces[surface.SurfaceId] = doc.RootElement.Clone();
                }
            }

            if (v10Surfaces.Count > 0)
            {
                metadata["a2uiRendererDataModel"] = JsonSerializer.SerializeToElement(
                    new { version = A2UiProtocol.Version10, surfaces = v10Surfaces },
                    s_camelCase
                );
            }

            if (v09Surfaces.Count > 0)
            {
                metadata["a2uiClientDataModel"] = JsonSerializer.SerializeToElement(
                    new { version = A2UiProtocol.Version091, surfaces = v09Surfaces },
                    s_camelCase
                );
            }
        }

        return metadata;
    }

    private static string? GetMimeFromMetadata(Dictionary<string, JsonElement>? metadata)
    {
        if (
            metadata is not null
            && metadata.TryGetValue("mimeType", out JsonElement mimeElement)
            && mimeElement.ValueKind == JsonValueKind.String
        )
        {
            return mimeElement.GetString();
        }

        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

internal static partial class A2AAgentClientLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Fetching agent card from {Url}")]
    public static partial void FetchingAgentCard(ILogger logger, string url);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Sending text query: {TextPreview}")]
    public static partial void SendingTextQuery(ILogger logger, string textPreview);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Sending action to agent")]
    public static partial void SendingAction(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "No parts in agent response (correlationId={CorrelationId})"
    )]
    public static partial void ResponseHasNoParts(ILogger logger, string correlationId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize A2UI message from DataPart (messageIndex={MessageIndex}, correlationId={CorrelationId})"
    )]
    public static partial void DataPartDeserializeFailed(
        ILogger logger,
        int messageIndex,
        string correlationId,
        Exception exception
    );

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Debug,
        Message = "Extracted {MessageCount} A2UI message(s) (correlationId={CorrelationId})"
    )]
    public static partial void ExtractedMessages(ILogger logger, int messageCount, string correlationId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "SendMessage started: url={HttpUrl}, requestBytes={RequestBytes}, messageId={MessageId}, correlationId={CorrelationId}"
    )]
    public static partial void SendMessageStarted(
        ILogger logger,
        string httpUrl,
        int requestBytes,
        string messageId,
        string correlationId
    );

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Error,
        Message = "SendMessage failed: url={HttpUrl}, durationMs={DurationMs}, exceptionType={ExceptionType}, correlationId={CorrelationId}"
    )]
    public static partial void SendMessageFailed(
        ILogger logger,
        string httpUrl,
        long durationMs,
        string exceptionType,
        string correlationId,
        Exception exception
    );

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "Agent returned JSON-RPC error: {ErrorMessage} (correlationId={CorrelationId})"
    )]
    public static partial void AgentReturnedError(ILogger logger, string errorMessage, string correlationId);
}
