using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Components.Extensions;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>A2UI "Image" → Avalonia Image. Loads from URL asynchronously.</summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ImageCatalogEntry"/> class.
/// </remarks>
/// <param name="logger">Optional logger for image download diagnostics.</param>
/// <param name="httpClient">Optional HTTP client used for downloading remote images.</param>
/// <param name="options">Optional Avalonia renderer options controlling allowed schemes and size limits.</param>
public sealed class ImageCatalogEntry(
    ILogger<ImageCatalogEntry>? logger = null,
    HttpClient? httpClient = null,
    A2UiAvaloniaOptions? options = null
) : ICatalogEntry
{
    private static readonly HttpClient s_defaultHttp = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }
    )
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly ILogger<ImageCatalogEntry> _logger = logger ?? NullLogger<ImageCatalogEntry>.Instance;
    private readonly HttpClient _http = httpClient ?? options?.HttpClient ?? s_defaultHttp;
    private readonly A2UiAvaloniaOptions? _options = options;

    /// <inheritdoc />
    public string ComponentType => "Image";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ImageComponent)component;
        var stretch = typed.Fit switch
        {
            "cover" => Stretch.UniformToFill,
            "fill" => Stretch.Fill,
            "none" => Stretch.None,
            "scaleDown" => Stretch.Uniform,
            _ => Stretch.Uniform, // "contain" or default
        };
        var img = new Image { Stretch = stretch };
        string? url = context.Resolve(typed.Url);
        if (url is not null && MediaSecurityHelper.IsSafeMediaUrl(url, this._options?.AllowedMediaSchemes))
        {
            img.Tag = url;
            _ = this.LoadImageAsync(img, url, context.SurfaceCancellation);
        }
        return img;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ImageComponent)component;
        if (existing is not Image img)
        {
            return false;
        }

        img.Stretch = typed.Fit switch
        {
            "cover" => Stretch.UniformToFill,
            "fill" => Stretch.Fill,
            "none" => Stretch.None,
            "scaleDown" => Stretch.Uniform,
            _ => Stretch.Uniform,
        };

        string? url = context.Resolve(typed.Url);
        if (url is null)
        {
            img.Source = null;
            img.Tag = null;
            return true;
        }

        if (!MediaSecurityHelper.IsSafeMediaUrl(url, this._options?.AllowedMediaSchemes))
        {
            img.Source = null;
            img.Tag = null;
            return true;
        }

        // If the image already has a source and the URL tag matches, skip reload.
        if (img.Source is not null && (img.Tag as string) == url)
        {
            return true;
        }

        // URL changed or source not yet loaded — reload.
        img.Tag = url;
        _ = this.LoadImageAsync(img, url, context.SurfaceCancellation);
        return true;
    }

    /// <summary>Maximum default image download size (10 MB) to avoid unbounded memory allocation.</summary>
    private const int DefaultMaxImageBytes = A2UiAvaloniaOptions.DefaultMaxImageBytes;

    internal async Task LoadImageAsync(Image img, string url, CancellationToken ct)
    {
        try
        {
            int maxBytes = this._options?.MaxImageBytes is > 0
                ? this._options.MaxImageBytes
                : DefaultMaxImageBytes;
            int maxMb = Math.Max(1, maxBytes / (1024 * 1024));

            using var response = await this._http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
            {
                MediaLog.ImageExceedsSizeLimit(
                    this._logger,
                    url,
                    maxMb,
                    (int)Math.Min(contentLength, int.MaxValue)
                );
                return;
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var ms = new MemoryStream(
                response.Content.Headers.ContentLength is long cl and > 0 and <= int.MaxValue ? (int)cl : 0
            );
            byte[] buffer = new byte[81920];
            int totalRead = 0;
            while (true)
            {
                int read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > maxBytes)
                {
                    MediaLog.ImageExceedsSizeLimit(this._logger, url, maxMb, totalRead);
                    return;
                }

                ms.Write(buffer, 0, read);
            }

            ms.Position = 0;
            var bitmap = new Bitmap(ms);

            // Dispatcher.UIThread.InvokeAsync returns DispatcherOperation, not Task —
            // ConfigureAwait is not applicable.
#pragma warning disable CA2007
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Guard against out-of-order completion: another Update() may have
                // changed the target URL while this download was in flight.
                if ((img.Tag as string) != url)
                {
                    bitmap.Dispose();
                    return;
                }

                var old = img.Source as Bitmap;
                img.Source = bitmap;
                old?.Dispose();
            });
#pragma warning restore CA2007
        }
        catch (OperationCanceledException)
        {
            // Intentionally cancelled — not an error.
        }
        catch (Exception ex)
        {
            MediaLog.FailedToLoadImage(this._logger, url, ex);
        }
    }
}

/// <summary>A2UI "Table" → Avalonia DataGrid (custom catalog extension).</summary>
public sealed class TableCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Table";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TableComponent)component;
        var grid = new DataGrid { CanUserReorderColumns = true, IsReadOnly = true };
        if (typed.Columns is { } cols)
        {
            foreach (var col in cols)
            {
                grid.Columns.Add(new DataGridTextColumn { Header = col.Header, Binding = new Binding(col.Field) });
            }
        }
        return grid;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (TableComponent)component;
        if (existing is not DataGrid grid)
        {
            return false;
        }

        if (typed.Columns is not { } cols || cols.Length != grid.Columns.Count)
        {
            return false;
        }

        for (int i = 0; i < cols.Length; i++)
        {
            if (grid.Columns[i] is DataGridTextColumn col)
            {
                col.Header = cols[i].Header;
            }
        }

        return true;
    }
}

/// <summary>A2UI "Surface" → root container (custom catalog extension).</summary>
public sealed class SurfaceCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Surface";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in context.RenderChildren(component.Id))
        {
            RenderContext.DetachFromParent(child);
            panel.Children.Add(child);
        }

        return panel;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not StackPanel panel)
        {
            return false;
        }

        var children = context.RenderChildren(component.Id).ToList();
        if (!LayoutHelper.SequenceReferenceEqual(panel.Children, children))
        {
            panel.Children.Clear();
            foreach (var child in children)
            {
                RenderContext.DetachFromParent(child);
                panel.Children.Add(child);
            }
        }

        return true;
    }
}

internal static partial class MediaLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Image at '{Url}' exceeds {MaxMb} MB limit ({ActualBytes} bytes), skipping"
    )]
    public static partial void ImageExceedsSizeLimit(ILogger logger, string url, int maxMb, int actualBytes);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to load image from '{Url}'")]
    public static partial void FailedToLoadImage(ILogger logger, string url, Exception exception);
}
