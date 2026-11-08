using System.Text.Json;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// Serializes <see cref="UserActionEventArgs"/> into the appropriate wire format
/// (v1.0 <see cref="RendererToAgentMessage"/> or v0.9 <c>userAction</c> envelope).
/// </summary>
internal static class UserActionSerializer
{
    public static object Serialize(UserActionEventArgs e, string? protocolVersion = null)
    {
        if (protocolVersion == A2UiProtocol.Version10)
        {
            Dictionary<string, JsonElement>? contextDict = null;
            if (e.Payload is not null)
            {
                if (e.Payload is Dictionary<string, JsonElement> dict)
                {
                    contextDict = dict;
                }
                else
                {
                    var doc = JsonSerializer.SerializeToDocument(e.Payload);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        contextDict = [];
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            contextDict[prop.Name] = prop.Value.Clone();
                        }
                    }
                }
            }

            return new RendererToAgentMessage
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
        }

        return new
        {
            userAction = new
            {
                name = e.EventName,
                surfaceId = e.SurfaceId,
                sourceComponentId = e.ComponentId ?? string.Empty,
                timestamp = DateTimeOffset.UtcNow.ToString("o"),
                context = e.Payload,
            },
        };
    }
}
