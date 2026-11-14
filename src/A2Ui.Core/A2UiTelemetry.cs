namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Public OpenTelemetry instrumentation identifiers for <c>Blazejewicz.A2Ui.Core</c>.
/// </summary>
public static class A2UiTelemetry
{
    /// <summary>
    /// Name of the <see cref="System.Diagnostics.ActivitySource"/> used for core surface tracing (<c>"A2Ui.Core"</c>).
    /// </summary>
    public const string ActivitySourceName = "A2Ui.Core";

    /// <summary>
    /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> used for core surface and error metrics (<c>"A2Ui.Core"</c>).
    /// </summary>
    public const string MeterName = "A2Ui.Core";

    /// <summary>
    /// Counter instrument name (<c>a2ui.surface.operations</c>) tracking processed surface and RPC operations.
    /// </summary>
    public const string SurfaceOperationsInstrumentName = "a2ui.surface.operations";

    /// <summary>
    /// UpDownCounter instrument name (<c>a2ui.surfaces.active</c>) tracking currently active surfaces.
    /// </summary>
    public const string ActiveSurfacesInstrumentName = "a2ui.surfaces.active";

    /// <summary>
    /// Counter instrument name (<c>a2ui.errors</c>) tracking protocol and operational errors.
    /// </summary>
    public const string ErrorsInstrumentName = "a2ui.errors";
}
