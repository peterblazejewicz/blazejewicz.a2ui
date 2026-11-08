namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Protocol version, MIME type, extension URI, and catalog ID constants for A2UI v0.9, v0.9.1, and v1.0.
/// </summary>
public static class A2UiProtocol
{
    /// <summary>Protocol version 0.9.</summary>
    public const string Version09 = "v0.9";

    /// <summary>Protocol version 0.9.1.</summary>
    public const string Version091 = "v0.9.1";

    /// <summary>Latest supported v0.9.x protocol version.</summary>
    public const string LatestV09 = Version091;

    /// <summary>Protocol version v1.0 wire value (<c>"1.0"</c>, without leading <c>"v"</c>).</summary>
    public const string Version10 = "1.0";

    /// <summary>Spec-named constant alias for v0.9.</summary>
    public const string V0_9 = Version09;

    /// <summary>Spec-named constant alias for v0.9.1.</summary>
    public const string V0_9_1 = Version091;

    /// <summary>Spec-named wire constant alias for v1.0 (<c>"1.0"</c>, without leading <c>"v"</c>).</summary>
    public const string V1_0 = Version10;

    /// <summary>Canonical IANA MIME type for A2UI payloads (<c>application/a2ui+json</c>).</summary>
    public const string MimeType = "application/a2ui+json";

    /// <summary>Alternate A2UI v0.9 MIME type (<c>application/json+a2ui</c>).</summary>
    public const string LegacyMimeType = "application/json+a2ui";

    /// <summary>A2A extension URI for v0.9.</summary>
    public const string A2AExtensionUriV09 = "https://a2ui.org/a2a-extension/a2ui/v0.9";

    /// <summary>A2A extension URI for v0.9.1.</summary>
    public const string A2AExtensionUriV091 = "https://a2ui.org/a2a-extension/a2ui/v0.9.1";

    /// <summary>A2A extension URI for v1.0.</summary>
    public const string A2AExtensionUriV10 = "https://a2ui.org/a2a-extension/a2ui/v1.0";

    /// <summary>Spec-named extension URI alias for v0.9.</summary>
    public const string ExtensionUriV0_9 = A2AExtensionUriV09;

    /// <summary>Spec-named extension URI alias for v0.9.1.</summary>
    public const string ExtensionUriV0_9_1 = A2AExtensionUriV091;

    /// <summary>Spec-named extension URI alias for v1.0.</summary>
    public const string ExtensionUriV1_0 = A2AExtensionUriV10;

    /// <summary>Basic catalog ID for v0.9.</summary>
    public const string BasicCatalogIdV09 = "https://a2ui.org/specification/v0_9/catalogs/basic/catalog.json";

    /// <summary>Basic catalog ID for v0.9.1.</summary>
    public const string BasicCatalogIdV091 = "https://a2ui.org/specification/v0_9_1/catalogs/basic/catalog.json";

    /// <summary>Basic catalog ID for v1.0.</summary>
    public const string BasicCatalogIdV10 = "https://a2ui.org/catalogs/basic/v1/catalog.json";

    /// <summary>Spec-named basic catalog ID alias for v0.9.</summary>
    public const string BasicCatalogIdV0_9 = BasicCatalogIdV09;

    /// <summary>Spec-named basic catalog ID alias for v0.9.1.</summary>
    public const string BasicCatalogIdV0_9_1 = BasicCatalogIdV091;

    /// <summary>Spec-named basic catalog ID alias for v1.0.</summary>
    public const string BasicCatalogIdV1_0 = BasicCatalogIdV10;

    /// <summary>Returns true when the version string is "v0.9", "v0.9.1", or "0.9.1".</summary>
    public static bool IsSupportedV09Version(string? version) => version is Version09 or Version091 or "0.9.1";

    /// <summary>Returns true when the version string is supported ("v0.9", "v0.9.1", or "1.0").</summary>
    public static bool IsSupportedVersion(string? version) => IsSupportedV09Version(version) || version == Version10;

    /// <summary>Returns true when the MIME type matches canonical or legacy A2UI MIME types.</summary>
    public static bool IsSupportedMimeType(string? mimeType) => mimeType is MimeType or LegacyMimeType;
}
