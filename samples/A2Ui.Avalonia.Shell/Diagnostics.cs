using System.Diagnostics;

namespace Blazejewicz.A2Ui.Avalonia.Shell;

/// <summary>
/// Centralized <see cref="ActivitySource"/> for outbound A2A client requests in <see cref="Services.A2AAgentClient"/>.
/// </summary>
internal static class Diagnostics
{
    public static readonly ActivitySource Source = new(
        name: "A2Ui.Shell.A2AClient",
        version: typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    );
}
