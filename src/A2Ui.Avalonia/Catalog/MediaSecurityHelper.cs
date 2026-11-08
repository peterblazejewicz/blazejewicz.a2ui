namespace Blazejewicz.A2Ui.Avalonia.Catalog;

/// <summary>
/// Validates media URLs against the A2UI protocol security specification.
/// Allows http:, https:, data:, and relative URLs without host prefix (not // or \).
/// Rejects javascript:, vbscript:, file:, blob:, content:, null/whitespace, or control chars.
/// </summary>
public static class MediaSecurityHelper
{
    private static readonly string[] s_allowedSchemes = ["http", "https", "data"];

    /// <summary>
    /// Validates whether a media URL is safe according to the A2UI security policy.
    /// </summary>
    /// <param name="url">The URL string to evaluate.</param>
    /// <returns><c>true</c> if the URL is safe to load; otherwise <c>false</c>.</returns>
    public static bool IsSafeMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Reject if string contains any control characters
        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        // Reject backslash-prefixed paths (e.g., \\evil.com\x.png or \file)
        if (trimmed.StartsWith('\\'))
        {
            return false;
        }

        // Reject protocol-relative URLs (e.g., //evil.com/x.png)
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Reject drive-letter colon patterns (e.g., /C:/ or /c:)
        if (trimmed.StartsWith('/') && trimmed.Length >= 3 && char.IsLetter(trimmed[1]) && trimmed[2] == ':')
        {
            return false;
        }

        // If it parses as an absolute URI, check against the allowed scheme list
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return s_allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
        }

        // Reject scheme-like prefixes (colon before first slash/backslash/query) that did not parse as absolute
        int colonIdx = trimmed.IndexOf(':');
        int slashIdx = trimmed.IndexOfAny(['/', '\\', '?']);
        if (colonIdx >= 0 && (slashIdx < 0 || colonIdx < slashIdx))
        {
            return false;
        }

        // If it parses as a relative URI, allow it
        if (Uri.TryCreate(trimmed, UriKind.Relative, out _))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Alias for <see cref="IsSafeMediaUrl(string?)"/>.
    /// </summary>
    public static bool IsAllowedMediaUrl(string? url) => IsSafeMediaUrl(url);

    /// <summary>
    /// Formats a URL for display, returning "no url" if null or "blocked unsafe url" if unsafe.
    /// </summary>
    public static string FormatSafeUrl(string? rawUrl)
    {
        if (rawUrl is null)
        {
            return "no url";
        }

        return IsSafeMediaUrl(rawUrl) ? rawUrl : "blocked unsafe url";
    }
}
