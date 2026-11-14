using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Centralized <see cref="ActivitySource"/> and <see cref="Meter"/> instances for <see cref="Surfaces.SurfaceManager"/> operations.
/// </summary>
internal static class Diagnostics
{
    private static readonly string s_version =
        typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static readonly ActivitySource Source = new(
        name: A2UiTelemetry.ActivitySourceName,
        version: s_version
    );

    public static readonly Meter Meter = new(
        name: A2UiTelemetry.MeterName,
        version: s_version
    );

    public static readonly Counter<long> SurfaceOperations = Meter.CreateCounter<long>(
        name: A2UiTelemetry.SurfaceOperationsInstrumentName,
        unit: "{operation}",
        description: "Number of A2UI surface and RPC operations processed."
    );

    public static readonly UpDownCounter<int> ActiveSurfaces = Meter.CreateUpDownCounter<int>(
        name: A2UiTelemetry.ActiveSurfacesInstrumentName,
        unit: "{surface}",
        description: "Number of currently active A2UI surfaces."
    );

    public static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        name: A2UiTelemetry.ErrorsInstrumentName,
        unit: "{error}",
        description: "Number of A2UI protocol or operational errors reported."
    );
}
