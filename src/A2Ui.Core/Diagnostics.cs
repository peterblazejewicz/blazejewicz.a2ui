using System.Diagnostics;

namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Centralized <see cref="ActivitySource"/> for <see cref="Surfaces.SurfaceManager"/> operations.
/// </summary>
internal static class Diagnostics
{
    public static readonly ActivitySource Source = new(
        name: "A2Ui.Core",
        version: typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0"
    );
}
