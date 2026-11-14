using System.Text.Json;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// Serializes <see cref="UserActionEventArgs"/> into the appropriate wire format
/// (v1.0 <see cref="RendererToAgentMessage"/> or v0.9 / v0.9.1 <see cref="ClientToServerMessage"/> envelope).
/// </summary>
public static class UserActionSerializer
{
    public static object Serialize(UserActionEventArgs e, string? protocolVersion = null)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (protocolVersion == A2UiProtocol.Version10)
        {
            Dictionary<string, JsonElement> contextDict = [];
            if (e.Payload is Dictionary<string, JsonElement> dict)
            {
                contextDict = new Dictionary<string, JsonElement>(dict, StringComparer.Ordinal);
            }
            else if (e.Payload is not null)
            {
                using var doc = JsonSerializer.SerializeToDocument(e.Payload);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        contextDict[prop.Name] = prop.Value.Clone();
                    }
                }
            }

            var v10Envelope = new RendererToAgentMessage
            {
                Version = A2UiProtocol.Version10,
                Action = new RendererAction
                {
                    Name = e.EventName,
                    SurfaceId = e.SurfaceId,
                    SourceComponentId = e.ComponentId ?? string.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                    UserMessage = e.UserMessage,
                    Context = contextDict,
                },
            };
            v10Envelope.Validate();
            return v10Envelope;
        }

        JsonElement contextElement;
        if (e.Payload is JsonElement je)
        {
            contextElement = je.Clone();
        }
        else if (e.Payload is not null)
        {
            contextElement = JsonSerializer.SerializeToElement(e.Payload);
        }
        else
        {
            using var emptyDoc = JsonDocument.Parse("{}");
            contextElement = emptyDoc.RootElement.Clone();
        }

        string version = protocolVersion == A2UiProtocol.Version09
            ? A2UiProtocol.Version09
            : A2UiProtocol.Version091;

        var clientEnvelope = new ClientToServerMessage
        {
            Version = version,
            Action = new ClientAction
            {
                Name = e.EventName,
                SurfaceId = e.SurfaceId,
                SourceComponentId = e.ComponentId ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                Context = contextElement,
            },
        };
        clientEnvelope.Validate();
        return clientEnvelope;
    }
}
