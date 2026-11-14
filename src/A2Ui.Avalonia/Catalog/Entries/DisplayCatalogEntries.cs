using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>A2UI "Icon" → TextBlock with Unicode icon mapping.</summary>
public sealed class IconCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Icon";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (IconComponent)component;
        string? svgPath = TryGetSvgPath(typed.Name, context);
        if (svgPath is not null)
        {
            return new PathIcon
            {
                Data = ParseGeometry(svgPath),
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        string? name = context.Resolve(typed.Name);
        return new TextBlock
        {
            Text = MapIconName(name),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (IconComponent)component;
        string? svgPath = TryGetSvgPath(typed.Name, context);
        if (svgPath is not null)
        {
            if (existing is not PathIcon pathIcon)
            {
                return false;
            }

            pathIcon.Data = ParseGeometry(svgPath);
            return true;
        }

        if (existing is not TextBlock tb)
        {
            return false;
        }

        tb.Text = MapIconName(context.Resolve(typed.Name));
        return true;
    }

    private static string? TryGetSvgPath(DynamicValue? nameValue, IRenderContext context)
    {
        if (nameValue is DynamicValue.ObjectValue obj && obj.Value.TryGetValue("svgPath", out var svgObj))
        {
            if (svgObj is string s)
            {
                return s;
            }
            if (svgObj is JsonElement { ValueKind: JsonValueKind.String } el)
            {
                return el.GetString();
            }
        }

        string? resolved = context.Resolve(nameValue);
        if (!string.IsNullOrEmpty(resolved) && resolved.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(resolved);
                if (
                    doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("svgPath", out var prop)
                    && prop.ValueKind == JsonValueKind.String
                )
                {
                    return prop.GetString();
                }
            }
            catch
            {
                // Not valid JSON object with svgPath, fallback to icon name
            }
        }

        return null;
    }

    private static StreamGeometry? ParseGeometry(string svgPath)
    {
        try
        {
            return StreamGeometry.Parse(svgPath);
        }
        catch
        {
            return null;
        }
    }

    private static string MapIconName(string? name) =>
        name switch
        {
            "add" => "\u2795",
            "check" => "\u2714",
            "close" => "\u2716",
            "delete" => "\uD83D\uDDD1",
            "edit" => "\u270F",
            "error" => "\u26A0",
            "favorite" => "\u2764",
            "home" => "\uD83C\uDFE0",
            "info" => "\u2139",
            "mail" => "\u2709",
            "menu" => "\u2630",
            "pause" => "\u23F8",
            "play" => "\u25B6",
            "search" => "\uD83D\uDD0D",
            "send" => "\u27A4",
            "settings" => "\u2699",
            "star" => "\u2B50",
            "warning" => "\u26A0",
            "arrowBack" => "\u2190",
            "arrowForward" => "\u2192",
            "refresh" => "\u21BB",
            "download" => "\u2B07",
            "upload" => "\u2B06",
            "visibility" => "\uD83D\uDC41",
            "visibilityOff" => "\uD83D\uDE48",
            "lock" => "\uD83D\uDD12",
            "lockOpen" => "\uD83D\uDD13",
            "person" => "\uD83D\uDC64",
            "notifications" => "\uD83D\uDD14",
            _ => "\u25A0", // default filled square
        };
}

/// <summary>A2UI "Divider" → Separator.</summary>
public sealed class DividerCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Divider";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (DividerComponent)component;
        var separator = new Separator();
        ApplyAxis(separator, typed.Axis);
        return separator;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (DividerComponent)component;
        if (existing is not Separator sep)
        {
            return false;
        }

        ApplyAxis(sep, typed.Axis);
        return true;
    }

    private static void ApplyAxis(Separator separator, string? axis)
    {
        if (axis is "vertical")
        {
            separator.Width = 1;
            separator.Height = double.NaN;
            separator.Margin = new Thickness(4, 0);
            separator.HorizontalAlignment = HorizontalAlignment.Center;
            separator.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            separator.Width = double.NaN;
            separator.Height = double.NaN;
            separator.Margin = new Thickness(0, 4);
            separator.HorizontalAlignment = HorizontalAlignment.Stretch;
            separator.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}

/// <summary>A2UI "Video" → placeholder (Avalonia has no native video control).</summary>
public sealed class VideoCatalogEntry(A2UiAvaloniaOptions? options = null) : ICatalogEntry
{
    private readonly A2UiAvaloniaOptions? _options = options;

    /// <inheritdoc />
    public string ComponentType => "Video";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (VideoComponent)component;
        var schemes = this._options?.AllowedMediaSchemes;
        string urlText = MediaSecurityHelper.FormatSafeUrl(context.Resolve(typed.Url), schemes);
        string? poster = context.Resolve(typed.PosterUrl);
        string? posterText = poster is not null ? MediaSecurityHelper.FormatSafeUrl(poster, schemes) : null;
        string text = posterText is not null ? $"[Video: {urlText}, Poster: {posterText}]" : $"[Video: {urlText}]";
        return new TextBlock { Text = text, Classes = { "Caption" } };
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (VideoComponent)component;
        if (existing is not TextBlock tb)
        {
            return false;
        }

        var schemes = this._options?.AllowedMediaSchemes;
        string urlText = MediaSecurityHelper.FormatSafeUrl(context.Resolve(typed.Url), schemes);
        string? poster = context.Resolve(typed.PosterUrl);
        string? posterText = poster is not null ? MediaSecurityHelper.FormatSafeUrl(poster, schemes) : null;
        tb.Text = posterText is not null ? $"[Video: {urlText}, Poster: {posterText}]" : $"[Video: {urlText}]";
        return true;
    }
}

/// <summary>A2UI "AudioPlayer" → placeholder (Avalonia has no native audio control).</summary>
public sealed class AudioPlayerCatalogEntry(A2UiAvaloniaOptions? options = null) : ICatalogEntry
{
    private readonly A2UiAvaloniaOptions? _options = options;

    /// <inheritdoc />
    public string ComponentType => "AudioPlayer";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (AudioPlayerComponent)component;
        string urlText = MediaSecurityHelper.FormatSafeUrl(context.Resolve(typed.Url), this._options?.AllowedMediaSchemes);
        return new TextBlock { Text = $"[AudioPlayer: {urlText}]", Classes = { "Caption" } };
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (AudioPlayerComponent)component;
        if (existing is not TextBlock tb)
        {
            return false;
        }

        string urlText = MediaSecurityHelper.FormatSafeUrl(context.Resolve(typed.Url), this._options?.AllowedMediaSchemes);
        tb.Text = $"[AudioPlayer: {urlText}]";
        return true;
    }
}
