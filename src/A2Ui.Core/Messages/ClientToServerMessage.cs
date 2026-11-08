using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// A2UI v0.9 / v0.9.1 client-to-server message envelope.
/// Contains exactly one of: action or error.
/// </summary>
public sealed record ClientToServerMessage
{
    /// <summary>Protocol version string (default "v0.9", also supports "v0.9.1").</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "v0.9";

    /// <summary>User action payload, mutually exclusive with <see cref="Error"/>.</summary>
    [JsonPropertyName("action")]
    public ClientAction? Action { get; init; }

    /// <summary>Client error payload, mutually exclusive with <see cref="Action"/>.</summary>
    [JsonPropertyName("error")]
    public ClientError? Error { get; init; }

    /// <summary>
    /// Validate that this message conforms to the A2UI client envelope constraints:
    /// version must be "v0.9" or "v0.9.1" and exactly one of action or error must be present.
    /// </summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when constraints are violated.</exception>
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrEmpty(this.Version))
        {
            violations.Add("Missing required 'version' property");
        }
        else if (!A2UiProtocol.IsSupportedV09Version(this.Version))
        {
            violations.Add($"Unsupported version '{this.Version}'; expected 'v0.9' or 'v0.9.1'");
        }

        bool hasAction = this.Action is not null;
        bool hasError = this.Error is not null;

        if (hasAction && hasError)
        {
            violations.Add("Message contains both 'action' and 'error'; exactly one is allowed");
        }
        else if (!hasAction && !hasError)
        {
            violations.Add("Message must contain exactly one of 'action' or 'error'");
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }
}
