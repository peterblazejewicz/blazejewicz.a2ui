using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Avalonia.Shell.Services;

/// <summary>
/// Client interface for communicating with an A2A agent that supports A2UI extensions.
/// </summary>
public interface IA2AClient
{
    /// <summary>Gets the agent's display name from its agent card.</summary>
    Task<string> GetAgentNameAsync(CancellationToken ct = default);

    /// <summary>Sends a text query and returns the A2UI messages from the response.</summary>
    Task<IReadOnlyList<A2UiMessage>> SendTextAsync(string text, CancellationToken ct = default);

    /// <summary>Sends a text query and returns the raw JSON A2UI messages from the response (supporting v0.9, v0.9.1, and v1.0).</summary>
    Task<IReadOnlyList<string>> SendTextRawAsync(string text, CancellationToken ct = default);

    /// <summary>Sends a user action and returns the A2UI messages from the response.</summary>
    Task<IReadOnlyList<A2UiMessage>> SendActionAsync(object userAction, CancellationToken ct = default);

    /// <summary>Sends a user action and returns the raw JSON A2UI messages from the response (supporting v0.9, v0.9.1, and v1.0).</summary>
    Task<IReadOnlyList<string>> SendActionRawAsync(object userAction, CancellationToken ct = default);
}
