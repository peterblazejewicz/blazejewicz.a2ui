using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Reports a renderer-side error to the agent.
/// Maps to renderer_to_agent.json#/$defs/error.
/// </summary>
public sealed record RendererError
{
    /// <summary>Error code describing the failure category.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Human-readable error description.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Surface ID where the error occurred, mutually exclusive with <see cref="FunctionCallId"/>.</summary>
    [JsonPropertyName("surfaceId")]
    public string? SurfaceId { get; init; }

    /// <summary>Function call ID where the error occurred, mutually exclusive with <see cref="SurfaceId"/>.</summary>
    [JsonPropertyName("functionCallId")]
    public string? FunctionCallId { get; init; }

    /// <summary>JSON pointer path to the field that failed validation (e.g. '/components/0/text').</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// Validates error constraints per v1.0 schema:
    /// exactly one of SurfaceId or FunctionCallId must be present (mutually exclusive XOR),
    /// and when Code is VALIDATION_FAILED, UNALLOWED_PARENT, or UNALLOWED_CHILD,
    /// SurfaceId and Path are both required.
    /// </summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when validation constraints fail.</exception>
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrEmpty(this.Code))
        {
            violations.Add("Missing required 'code' property");
        }

        if (string.IsNullOrEmpty(this.Message))
        {
            violations.Add("Missing required 'message' property");
        }

        bool hasSurfaceId = !string.IsNullOrEmpty(this.SurfaceId);
        bool hasFunctionCallId = !string.IsNullOrEmpty(this.FunctionCallId);

        if (!hasSurfaceId && !hasFunctionCallId)
        {
            violations.Add("RendererError must contain exactly one of 'surfaceId' or 'functionCallId'");
        }
        else if (hasSurfaceId && hasFunctionCallId)
        {
            violations.Add("RendererError cannot contain both 'surfaceId' and 'functionCallId' (mutually exclusive)");
        }

        bool isStructuralValidationCode = this.Code is "VALIDATION_FAILED" or "UNALLOWED_PARENT" or "UNALLOWED_CHILD";

        if (isStructuralValidationCode)
        {
            if (!hasSurfaceId)
            {
                violations.Add($"RendererError code '{this.Code}' requires 'surfaceId'");
            }
            if (string.IsNullOrEmpty(this.Path))
            {
                violations.Add($"RendererError code '{this.Code}' requires 'path'");
            }
            if (hasFunctionCallId)
            {
                violations.Add($"RendererError code '{this.Code}' cannot have 'functionCallId'");
            }
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}
