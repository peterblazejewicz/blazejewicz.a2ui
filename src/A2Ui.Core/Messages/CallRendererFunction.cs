using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Signals the renderer to execute a function locally on behalf of the agent.
/// Maps to agent_to_renderer.json#/$defs/CallRendererFunctionMessage.
/// </summary>
public sealed record CallRendererFunction
{
    /// <summary>Optional surface ID whose data model provides the evaluation context.</summary>
    [JsonPropertyName("surfaceId")]
    public string? SurfaceId { get; init; }

    /// <summary>Unique ID for this instance of the function call.</summary>
    [JsonPropertyName("functionCallId")]
    public required string FunctionCallId { get; init; }

    /// <summary>Function invocation payload. Requires catalogId in v1.0 agent_to_renderer.</summary>
    [JsonPropertyName("callFunction")]
    public required FunctionCallValue CallFunction { get; init; }

    /// <summary>Validates the callRendererFunction payload.</summary>
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
        else
        {
            if (string.IsNullOrEmpty(this.CallFunction.Call))
            {
                violations.Add("Missing required function call name ('@call' or 'call')");
            }

            if (string.IsNullOrEmpty(this.CallFunction.CatalogId))
            {
                violations.Add("callRendererFunction.callFunction requires 'catalogId'");
            }
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}
