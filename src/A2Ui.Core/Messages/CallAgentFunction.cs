using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Signals the agent to execute a function remotely on behalf of the renderer.
/// Maps to renderer_to_agent.json#/$defs/callAgentFunction.
/// </summary>
public sealed record CallAgentFunction
{
    /// <summary>The surface ID where the call originated (optional per v1.0 spec).</summary>
    [JsonPropertyName("surfaceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SurfaceId { get; init; }

    /// <summary>Unique ID for this instance of the function call.</summary>
    [JsonPropertyName("functionCallId")]
    public required string FunctionCallId { get; init; }

    /// <summary>Function invocation payload.</summary>
    [JsonPropertyName("callFunction")]
    public required FunctionCallValue CallFunction { get; init; }

    /// <summary>Validates the callAgentFunction payload.</summary>
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrEmpty(this.FunctionCallId))
        {
            violations.Add("Missing required 'functionCallId' property");
        }

        if (this.CallFunction is null)
        {
            violations.Add("Missing required 'callFunction' property");
        }
        else if (string.IsNullOrEmpty(this.CallFunction.Call))
        {
            violations.Add("Missing required function call name ('@call' or 'call')");
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}
