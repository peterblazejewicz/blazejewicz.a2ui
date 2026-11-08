using System.Diagnostics;

namespace Blazejewicz.A2Ui.AgUi.Protocol;

/// <summary>
/// Centralized <see cref="ActivitySource"/> for AG-UI tool-call argument accumulation in <see cref="ToolCallArgsAccumulator"/>.
/// </summary>
internal static class Diagnostics
{
    public static readonly ActivitySource Source = new(
        name: "A2Ui.AgUi.Protocol",
        version: typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    );
}
