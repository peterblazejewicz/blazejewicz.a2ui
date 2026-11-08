using System.Text.Json;

namespace Blazejewicz.A2Ui.Core.Versioning;

/// <summary>
/// Protocol version adapter interface. Converts incoming wire envelopes into
/// version-neutral <see cref="InternalOperation"/> instances and handles version-specific
/// validation rules.
/// </summary>
public interface IVersionAdapter
{
    /// <summary>The protocol version handled by this adapter (e.g. "v0.9", "1.0").</summary>
    string Version { get; }

    /// <summary>Adapts an incoming agent-to-renderer JSON element into an internal operation.</summary>
    InternalOperation AdaptAgentToRenderer(JsonElement root);

    /// <summary>Adapts an incoming agent-to-renderer JSON string into an internal operation.</summary>
    InternalOperation AdaptAgentToRenderer(string json);
}
