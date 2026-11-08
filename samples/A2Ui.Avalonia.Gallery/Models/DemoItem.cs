using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Avalonia.Gallery.Models;

/// <summary>A single gallery example with its A2UI messages.</summary>
public sealed record DemoItem(
    string Id,
    string Title,
    string Filename,
    string Description,
    IReadOnlyList<A2UiMessage> Messages,
    bool IsBasic,
    string ProtocolVersion = "v0.9.1",
    IReadOnlyList<string>? RawJsonMessages = null
)
{
    /// <summary>Total number of messages in this demo.</summary>
    public int MessageCount =>
        this.RawJsonMessages is { Count: > 0 } ? this.RawJsonMessages.Count : this.Messages.Count;
}
