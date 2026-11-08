using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia;

/// <summary>
/// Helper extensions for inspecting a surface's protocol version across v0.9 and v1.0.
/// </summary>
public static class SurfaceVersionExtensions
{
    /// <summary>
    /// Gets the protocol version of the surface ("1.0", "v0.9.1", etc.).
    /// </summary>
    public static string GetProtocolVersion(this Surface surface)
    {
        if (!string.IsNullOrEmpty(surface.ProtocolVersion))
        {
            return surface.ProtocolVersion;
        }

        if (surface.Theme is not null)
        {
            return A2UiProtocol.Version091;
        }

        if (surface.CatalogId is not null)
        {
            if (
                surface.CatalogId.Contains("v0_9", StringComparison.Ordinal)
                || surface.CatalogId.Contains("0.9", StringComparison.Ordinal)
                || surface.CatalogId.Contains("minimal", StringComparison.Ordinal)
            )
            {
                return A2UiProtocol.Version091;
            }

            if (
                surface.CatalogId.Contains("v1_0", StringComparison.Ordinal)
                || surface.CatalogId.Contains("1.0", StringComparison.Ordinal)
            )
            {
                return A2UiProtocol.Version10;
            }
        }

        return A2UiProtocol.Version091;
    }
}
