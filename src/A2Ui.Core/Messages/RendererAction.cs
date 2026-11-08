using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Reports a user-initiated action from a component.
/// Maps to renderer_to_agent.json#/$defs/action.
/// </summary>
public sealed record RendererAction
{
    /// <summary>Name of the action from component action.event.name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Optional human-readable description of the action resolved by the renderer.</summary>
    [JsonPropertyName("userMessage")]
    public string? UserMessage { get; init; }

    /// <summary>Surface ID where the event originated.</summary>
    [JsonPropertyName("surfaceId")]
    public required string SurfaceId { get; init; }

    /// <summary>Component ID that triggered the event.</summary>
    [JsonPropertyName("sourceComponentId")]
    public required string SourceComponentId { get; init; }

    /// <summary>ISO 8601 timestamp of when the event occurred.</summary>
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    /// <summary>Key-value context dictionary after resolving all data bindings.</summary>
    [JsonPropertyName("context")]
    public Dictionary<string, JsonElement>? Context { get; init; }

    /// <summary>Optional renderer-side metadata to send back to the agent.</summary>
    [JsonPropertyName("metadata")]
    public ActionMetadata? Metadata { get; init; }

    /// <summary>Validates required action properties.</summary>
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrEmpty(this.Name))
        {
            violations.Add("Missing required 'name' property");
        }
        if (string.IsNullOrEmpty(this.SurfaceId))
        {
            violations.Add("Missing required 'surfaceId' property");
        }
        if (string.IsNullOrEmpty(this.SourceComponentId))
        {
            violations.Add("Missing required 'sourceComponentId' property");
        }
        if (string.IsNullOrEmpty(this.Timestamp))
        {
            violations.Add("Missing required 'timestamp' property");
        }
        if (this.Context is null)
        {
            violations.Add("Missing required 'context' property");
        }

        this.Metadata?.Validate();

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}
