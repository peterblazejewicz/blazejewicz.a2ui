using Blazejewicz.A2Ui.Core.Functions;

namespace Blazejewicz.A2Ui.Avalonia;

/// <summary>
/// Configuration options for the <c>A2Ui.Avalonia</c> renderer and built-in catalog entries.
/// </summary>
public sealed class A2UiAvaloniaOptions
{
    /// <summary>
    /// Default maximum allowed size in bytes for downloaded images (10 MB).
    /// </summary>
    public const int DefaultMaxImageBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Gets the set of URI schemes permitted for media URLs (<c>Image</c>, <c>Video</c>, <c>AudioPlayer</c>).
    /// Defaults to <c>http</c>, <c>https</c>, and <c>data</c> (case-insensitive).
    /// </summary>
    public HashSet<string> AllowedMediaSchemes { get; } =
        new(["http", "https", "data"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the maximum allowed size in bytes for downloaded images.
    /// Defaults to <see cref="DefaultMaxImageBytes"/> (10 MB).
    /// </summary>
    public int MaxImageBytes { get; set; } = DefaultMaxImageBytes;

    /// <summary>
    /// Gets or sets an optional <see cref="HttpClient"/> instance used by
    /// <see cref="Catalog.Entries.ImageCatalogEntry"/> when downloading remote images.
    /// When <see langword="null"/>, a shared client with <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>
    /// configured is used.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="IUrlLauncher"/> used when evaluating the <c>openUrl</c> catalog function.
    /// </summary>
    public IUrlLauncher? UrlLauncher { get; set; }
}
