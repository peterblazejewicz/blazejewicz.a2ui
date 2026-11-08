using System.Diagnostics;

namespace Blazejewicz.A2Ui.Avalonia;

/// <summary>
/// Centralized <see cref="ActivitySource"/> instances for <see cref="A2UiRenderer"/> (<c>A2Ui.Avalonia.Renderer</c>)
/// and <see cref="AgentEventBridge"/> (<c>A2Ui.Avalonia.Bridge</c>).
/// </summary>
internal static class Diagnostics
{
    public static readonly ActivitySource RendererSource = new(
        name: "A2Ui.Avalonia.Renderer",
        version: typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    );

    public static readonly ActivitySource BridgeSource = new(
        name: "A2Ui.Avalonia.Bridge",
        version: typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    );
}
