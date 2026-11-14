using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Static methods implementing the A2UI built-in function catalog.
/// Each method takes pre-resolved string arguments and returns a string result.
/// </summary>
public static partial class BuiltInFunctions
{
    // ── Cached culture info ────────────────────────────────────────────

    private static readonly NumberFormatInfo s_enUsNumberFormat = CreateEnUsNumberFormat();

    /// <summary>
    /// Create en-US number format, falling back to InvariantCulture in
    /// globalization-invariant mode (Docker containers, trimmed apps).
    /// </summary>
    private static NumberFormatInfo CreateEnUsNumberFormat()
    {
        try
        {
            return CultureInfo.GetCultureInfo("en-US").NumberFormat;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture.NumberFormat;
        }
    }

    // ── Formatting ──────────────────────────────────────────────────────

    /// <summary>Capitalizes the first character of a string.</summary>
    public static string? Capitalize(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    }

    /// <summary>Formats a numeric value with optional decimals and grouping.</summary>
    public static string? FormatNumber(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (!TryParseDouble(value, out double num))
        {
            return "";
        }

        int decimals = TryParseInt(GetArg(args, "decimals"), 0);
        // grouping defaults to true; coerce via DynamicBoolean rules when provided
        string? groupingArg = GetArg(args, "grouping");
        bool grouping = groupingArg is null || CoerceDynamicBoolean(groupingArg);

        if (grouping && decimals == s_enUsNumberFormat.NumberDecimalDigits)
        {
            // Common case: reuse cached format info
            return num.ToString($"N{decimals}", s_enUsNumberFormat);
        }

        var nfi = (NumberFormatInfo)s_enUsNumberFormat.Clone();
        if (!grouping)
        {
            nfi.NumberGroupSeparator = "";
        }

        return num.ToString($"N{decimals}", nfi);
    }

    /// <summary>Formats a numeric value as currency.</summary>
    public static string? FormatCurrency(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (!TryParseDouble(value, out double num))
        {
            return "";
        }

        string currency = GetArg(args, "currency") ?? "USD";
        int decimals = TryParseInt(GetArg(args, "decimals"), 2);
        string? groupingArg = GetArg(args, "grouping");
        bool grouping = groupingArg is null || CoerceDynamicBoolean(groupingArg);

        string symbol = currency.ToUpperInvariant() switch
        {
            "USD" => "$",
            "EUR" => "\u20ac",
            "GBP" => "\u00a3",
            "JPY" => "\u00a5",
            _ => currency + " ",
        };

        // Currency always needs clone because we set CurrencySymbol
        var nfi = (NumberFormatInfo)s_enUsNumberFormat.Clone();
        nfi.CurrencySymbol = symbol;
        nfi.CurrencyDecimalDigits = decimals;
        if (!grouping)
        {
            nfi.CurrencyGroupSeparator = "";
        }

        return num.ToString("C", nfi);
    }

    /// <summary>Formats an ISO 8601 timestamp using TR35 tokens or ISO normalization.</summary>
    public static string? FormatDate(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (!TryParseIsoTimestamp(value, out DateTime shiftedWallClock, out DateTime utcInstant))
        {
            return "";
        }

        string? format = GetArg(args, "format");
        if (string.IsNullOrEmpty(format))
        {
            return shiftedWallClock.ToString(CultureInfo.InvariantCulture);
        }

        if (format == "ISO")
        {
            return utcInstant.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        // Map Unicode TR35 tokens to .NET format tokens
        string dotNetFormat = MapTr35ToNet(format);
        return shiftedWallClock.ToString(dotNetFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Selects a pluralized string form based on CLDR plural categories.</summary>
    public static string? Pluralize(IReadOnlyDictionary<string, string?> args) =>
        Pluralize(args, null);

    /// <summary>Selects a pluralized string form based on CLDR plural categories and context culture.</summary>
    public static string? Pluralize(IReadOnlyDictionary<string, string?> args, FunctionEvaluationContext? context)
    {
        string? value = GetArg(args, "value") ?? GetArg(args, "count");
        if (!TryParseDouble(value, out double n) || double.IsNaN(n) || double.IsInfinity(n))
        {
            return GetArg(args, "other") ?? "";
        }

        // Explicit 0, 1, 2 cases take precedence when key is present (even if value is empty string)
        if (n == 0.0 && args.TryGetValue("zero", out string? zero) && zero is not null)
        {
            return zero;
        }

        if (n == 1.0 && args.TryGetValue("one", out string? one) && one is not null)
        {
            return one;
        }

        if (n == 2.0 && args.TryGetValue("two", out string? two) && two is not null)
        {
            return two;
        }

        string locale = GetArg(args, "locale") ?? context?.Culture?.Name ?? "en-US";
        string category = SelectCldrPluralCategory(n, locale);
        if (args.TryGetValue(category, out string? form) && form is not null)
        {
            return form;
        }

        return GetArg(args, "other") ?? "";
    }

    // ── Arithmetic ──────────────────────────────────────────────────────

    /// <summary>Adds two numbers.</summary>
    public static string? Add(IReadOnlyDictionary<string, string?> args) => BinaryMath(args, (a, b) => a + b);

    /// <summary>Subtracts b from a.</summary>
    public static string? Subtract(IReadOnlyDictionary<string, string?> args) => BinaryMath(args, (a, b) => a - b);

    /// <summary>Multiplies two numbers.</summary>
    public static string? Multiply(IReadOnlyDictionary<string, string?> args) => BinaryMath(args, (a, b) => a * b);

    /// <summary>Divides a by b.</summary>
    public static string? Divide(IReadOnlyDictionary<string, string?> args)
    {
        if (!TryParseDouble(GetArg(args, "a"), out double a) || !TryParseDouble(GetArg(args, "b"), out double b))
        {
            return null;
        }
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (b == 0.0)
        {
            return "\u221e"; // ∞
        }

        return (a / b).ToString(CultureInfo.InvariantCulture);
    }

    // ── Comparison ──────────────────────────────────────────────────────

    /// <summary>Checks string equality of a and b.</summary>
    public static string? Equals(IReadOnlyDictionary<string, string?> args) =>
        BoolResult(GetArg(args, "a") == GetArg(args, "b"));

    /// <summary>Checks string inequality of a and b.</summary>
    public static string? NotEquals(IReadOnlyDictionary<string, string?> args) =>
        BoolResult(GetArg(args, "a") != GetArg(args, "b"));

    /// <summary>Checks whether numeric a is greater than b.</summary>
    public static string? GreaterThan(IReadOnlyDictionary<string, string?> args)
    {
        if (!TryParseDouble(GetArg(args, "a"), out double a) || !TryParseDouble(GetArg(args, "b"), out double b))
        {
            return "false";
        }

        return BoolResult(a > b);
    }

    /// <summary>Checks whether numeric a is less than b.</summary>
    public static string? LessThan(IReadOnlyDictionary<string, string?> args)
    {
        if (!TryParseDouble(GetArg(args, "a"), out double a) || !TryParseDouble(GetArg(args, "b"), out double b))
        {
            return "false";
        }

        return BoolResult(a > b is false && a != b);
    }

    // ── Logical ─────────────────────────────────────────────────────────

    /// <summary>Evaluates logical AND over an array of DynamicBoolean operands.</summary>
    public static string? And(IReadOnlyDictionary<string, string?> args)
    {
        var items = ParseLogicOperands("and", GetArg(args, "values"));
        return BoolResult(items.All(CoerceDynamicBooleanElement));
    }

    /// <summary>Evaluates logical OR over an array of DynamicBoolean operands.</summary>
    public static string? Or(IReadOnlyDictionary<string, string?> args)
    {
        var items = ParseLogicOperands("or", GetArg(args, "values"));
        return BoolResult(items.Any(CoerceDynamicBooleanElement));
    }

    /// <summary>Evaluates logical NOT on a DynamicBoolean operand.</summary>
    public static string? Not(IReadOnlyDictionary<string, string?> args) =>
        BoolResult(!CoerceDynamicBoolean(GetArg(args, "value")));

    // ── String predicates ───────────────────────────────────────────────

    /// <summary>Checks whether string contains substring.</summary>
    public static string? Contains(IReadOnlyDictionary<string, string?> args)
    {
        string? str = GetArg(args, "string");
        string? sub = GetArg(args, "substring");
        if (str is null || sub is null)
        {
            return "false";
        }

        return BoolResult(str.Contains(sub, StringComparison.Ordinal));
    }

    /// <summary>Checks whether string starts with prefix.</summary>
    public static string? StartsWith(IReadOnlyDictionary<string, string?> args)
    {
        string? str = GetArg(args, "string");
        string? prefix = GetArg(args, "prefix");
        if (str is null || prefix is null)
        {
            return "false";
        }

        return BoolResult(str.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Checks whether string ends with suffix.</summary>
    public static string? EndsWith(IReadOnlyDictionary<string, string?> args)
    {
        string? str = GetArg(args, "string");
        string? suffix = GetArg(args, "suffix");
        if (str is null || suffix is null)
        {
            return "false";
        }

        return BoolResult(str.EndsWith(suffix, StringComparison.Ordinal));
    }

    // ── Validation ──────────────────────────────────────────────────────

    /// <summary>Validates that a value is non-null, non-empty string, and non-empty array.</summary>
    public static string? Required(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (string.IsNullOrEmpty(value))
        {
            return "false";
        }

        if (value.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return BoolResult(doc.RootElement.GetArrayLength() > 0);
                }
            }
            catch (JsonException)
            {
                // Treat as non-empty string
            }
        }

        return "true";
    }

    /// <summary>Validates email syntax.</summary>
    public static string? Email(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (string.IsNullOrEmpty(value))
        {
            return "false";
        }

        return BoolResult(
            Regex.IsMatch(
                value,
                @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$",
                RegexOptions.NonBacktracking,
                TimeSpan.FromSeconds(1)
            )
        );
    }

    /// <summary>Validates a string against an A2UI portable regex pattern.</summary>
    public static string? RegexMatch(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        string? pattern = GetArg(args, "pattern");
        if (value is null || pattern is null)
        {
            return "false";
        }

        return BoolResult(RegexSubset.IsMatch(pattern, value));
    }

    /// <summary>Validates string length or array element count against min and max bounds.</summary>
    public static string? Length(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        int len = 0;
        if (!string.IsNullOrEmpty(value))
        {
            if (value.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(value);
                    len = doc.RootElement.ValueKind == JsonValueKind.Array
                        ? doc.RootElement.GetArrayLength()
                        : RegexSubset.CountCodePoints(value);
                }
                catch (JsonException)
                {
                    len = RegexSubset.CountCodePoints(value);
                }
            }
            else
            {
                len = RegexSubset.CountCodePoints(value);
            }
        }

        int min = TryParseInt(GetArg(args, "min"), 0);
        int max = TryParseInt(GetArg(args, "max"), int.MaxValue);
        return BoolResult(len >= min && len <= max);
    }

    /// <summary>Validates that a value is numeric and within optional min and max bounds.</summary>
    public static string? Numeric(IReadOnlyDictionary<string, string?> args)
    {
        string? value = GetArg(args, "value");
        if (!TryParseDouble(value, out double num) || double.IsNaN(num) || double.IsInfinity(num))
        {
            return "false";
        }

        double min = TryParseDouble(GetArg(args, "min"), out double mn) ? mn : double.MinValue;
        double max = TryParseDouble(GetArg(args, "max"), out double mx) ? mx : double.MaxValue;
        return BoolResult(num >= min && num <= max);
    }

    // ── Void ────────────────────────────────────────────────────────────

    /// <summary>Validates and launches a URL when invoked from an allowed context.</summary>
    public static string? OpenUrl(IReadOnlyDictionary<string, string?> args) =>
        OpenUrl(args, null);

    /// <summary>Validates and launches a URL when invoked from an allowed context.</summary>
    public static string? OpenUrl(IReadOnlyDictionary<string, string?> args, FunctionEvaluationContext? context)
    {
        if (context is not null && context.CallerRole != FunctionCallerRole.RendererAction)
        {
            throw new A2UiEvaluationException("openUrl requires user activation and cannot be called in a non-action context.");
        }

        string? url = GetArg(args, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw new A2UiEvaluationException($"Invalid URL specified for openUrl: '{url}'");
        }

        string scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("https" or "http" or "mailto" or "tel"))
        {
            throw new A2UiEvaluationException($"Unsupported URL scheme '{scheme}' in openUrl.");
        }

        context?.UrlLauncher?.TryLaunchUrl(uri.AbsoluteUri);
        return null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Coerces a resolved string or JSON representation according to A2UI DynamicBoolean rules:
    /// - ValidationResult JSON object (<c>{"valid": true|false}</c>) -> its <c>valid</c> boolean
    /// - String <c>"true"</c> (case-insensitive) -> <see langword="true"/>
    /// - Non-zero finite number -> <see langword="true"/>
    /// - Everything else (<c>"false"</c>, <c>"0"</c>, <c>""</c>, <c>null</c>, arbitrary strings, non-ValidationResult objects, arrays) -> <see langword="false"/>
    /// </summary>
    public static bool CoerceDynamicBoolean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Length > 2 && value[0] == '{' && value[^1] == '}')
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (
                    doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("valid", out var validProp)
                    && (validProp.ValueKind == JsonValueKind.True || validProp.ValueKind == JsonValueKind.False)
                )
                {
                    return validProp.GetBoolean();
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        if (TryParseDouble(value, out double num))
        {
            return num != 0.0 && !double.IsNaN(num);
        }

        return false;
    }

    private static bool CoerceDynamicBooleanElement(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetDouble(out double d) && d != 0.0 && !double.IsNaN(d),
            JsonValueKind.String => CoerceDynamicBoolean(el.GetString()),
            JsonValueKind.Object => el.TryGetProperty("valid", out var vp)
                && (vp.ValueKind == JsonValueKind.True || vp.ValueKind == JsonValueKind.False)
                && vp.GetBoolean(),
            _ => false,
        };

    private static List<JsonElement> ParseLogicOperands(string funcName, string? valuesJson)
    {
        if (string.IsNullOrWhiteSpace(valuesJson))
        {
            throw new A2UiEvaluationException($"{funcName} requires at least 2 values.");
        }

        using var doc = JsonDocument.Parse(valuesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2)
        {
            throw new A2UiEvaluationException($"{funcName} requires at least 2 values.");
        }

        var list = new List<JsonElement>(doc.RootElement.GetArrayLength());
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            list.Add(item.Clone());
        }

        return list;
    }

    private static bool TryParseIsoTimestamp(
        string value,
        out DateTime shiftedWallClock,
        out DateTime utcInstant
    )
    {
        shiftedWallClock = default;
        utcInstant = default;

        Match written = WrittenDateFieldsRegex().Match(value.Trim());
        if (!written.Success)
        {
            return false;
        }

        if (
            !int.TryParse(written.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
            || !int.TryParse(written.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int month)
            || !int.TryParse(written.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
        )
        {
            return false;
        }

        if (month is < 1 or > 12 || year is < 1 or > 9999 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        int hour = written.Groups[4].Success
            ? int.Parse(written.Groups[4].Value, CultureInfo.InvariantCulture)
            : 0;
        int minute = written.Groups[5].Success
            ? int.Parse(written.Groups[5].Value, CultureInfo.InvariantCulture)
            : 0;
        int second = written.Groups[6].Success
            ? int.Parse(written.Groups[6].Value, CultureInfo.InvariantCulture)
            : 0;

        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return false;
        }

        if (
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset dto
            )
        )
        {
            return false;
        }

        Match offsetMatch = TimeOffsetRegex().Match(value.Trim());
        TimeSpan offset = TimeSpan.Zero;
        if (offsetMatch.Success && offsetMatch.Groups[2].Success)
        {
            int sign = offsetMatch.Groups[2].Value == "-" ? -1 : 1;
            int offHours = int.Parse(offsetMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            int offMins = offsetMatch.Groups[4].Success
                ? int.Parse(offsetMatch.Groups[4].Value, CultureInfo.InvariantCulture)
                : 0;
            offset = TimeSpan.FromMinutes(sign * ((offHours * 60) + offMins));
        }

        utcInstant = dto.UtcDateTime;
        DateTime shifted = utcInstant.Add(offset);

        // Verify written calendar fields round-trip without rolling into the next day/month
        if (
            shifted.Year != year
            || shifted.Month != month
            || shifted.Day != day
            || (written.Groups[4].Success && shifted.Hour != hour)
            || (written.Groups[5].Success && shifted.Minute != minute)
            || (written.Groups[6].Success && shifted.Second != second)
        )
        {
            return false;
        }

        shiftedWallClock = DateTime.SpecifyKind(shifted, DateTimeKind.Utc);
        return true;
    }

    private static string SelectCldrPluralCategory(double n, string locale)
    {
        double absN = Math.Abs(n);
        long i = (long)Math.Truncate(absN);
        bool isInt = absN == i;

        string lang = locale.Split(['-', '_'])[0].ToLowerInvariant();
        return lang switch
        {
            "ru" or "uk" or "pl" or "cs" or "sk" when isInt => (i % 10, i % 100) switch
            {
                (1, not 11) => "one",
                ( >= 2 and <= 4, < 12 or > 14) => "few",
                _ => "many",
            },
            "ar" when isInt => (i, i % 100) switch
            {
                (0, _) => "zero",
                (1, _) => "one",
                (2, _) => "two",
                (_, >= 3 and <= 10) => "few",
                (_, >= 11 and <= 99) => "many",
                _ => "other",
            },
            _ => isInt && i == 1 ? "one" : "other",
        };
    }

    private static string? GetArg(IReadOnlyDictionary<string, string?> args, string key) =>
        args.TryGetValue(key, out string? v) ? v : null;

    private static bool TryParseDouble(string? s, out double result) =>
        double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    private static int TryParseInt(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static string BoolResult(bool b) => b ? "true" : "false";

    private static string? BinaryMath(IReadOnlyDictionary<string, string?> args, Func<double, double, double> op)
    {
        if (!TryParseDouble(GetArg(args, "a"), out double a) || !TryParseDouble(GetArg(args, "b"), out double b))
        {
            return null;
        }

        return op(a, b).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Maps Unicode TR35 date format tokens to .NET format strings while preserving
    /// single-quoted literal spans (<c>'...'</c>) and escaped apostrophes (<c>''</c>).
    /// </summary>
    private static string MapTr35ToNet(string format)
    {
        if (format.Length == 0)
        {
            return format;
        }

        var output = new StringBuilder(format.Length + 8);
        var unquoted = new StringBuilder();
        bool inQuoted = false;
        int i = 0;

        while (i < format.Length)
        {
            char c = format[i];

            if (c == '\'')
            {
                // Flush any pending unquoted run before we enter/leave the quoted span.
                if (!inQuoted && unquoted.Length > 0)
                {
                    output.Append(TranslateUnquoted(unquoted.ToString()));
                    unquoted.Clear();
                }

                // TR35 (and .NET) use '' to denote a literal apostrophe. Emit both
                // quote characters verbatim and stay in the current mode.
                if (i + 1 < format.Length && format[i + 1] == '\'')
                {
                    output.Append("''");
                    i += 2;
                    continue;
                }

                // Bare single quote toggles the quoted-literal mode. Emit it verbatim
                // so .NET's formatter sees the same escaping the caller wrote.
                output.Append('\'');
                inQuoted = !inQuoted;
                i++;
                continue;
            }

            if (inQuoted)
            {
                output.Append(c);
            }
            else
            {
                unquoted.Append(c);
            }

            i++;
        }

        if (unquoted.Length > 0)
        {
            output.Append(TranslateUnquoted(unquoted.ToString()));
        }

        return output.ToString();
    }

    private static string TranslateUnquoted(string chunk)
    {
        // Use placeholders so EEEE → dddd (and E → ddd) does not collide with
        // dd/d token handling by .NET; the mapping order is otherwise irrelevant.
        string r = chunk;
        r = r.Replace("EEEE", "\x01FULL_DOW\x01");
        r = r.Replace("E", "\x01SHORT_DOW\x01");
        r = r.Replace("\x01FULL_DOW\x01", "dddd");
        r = r.Replace("\x01SHORT_DOW\x01", "ddd");

        // TR35 collapses every run of a's (a, aa, aaa, aaaa, aaaaa) onto a single
        // AM/PM designator. Map all runs to .NET's two-letter "tt" — longer runs
        // like "tttttttt" are rejected by DateTime.ToString.
        r = AmPmRunRegex().Replace(r, "tt");
        return r;
    }

    [GeneratedRegex("a+")]
    private static partial Regex AmPmRunRegex();

    [GeneratedRegex(@"^([+-]?\d{4,6})-(\d{2})-(\d{2})(?:[T ](\d{2})(?::(\d{2})(?::(\d{2}))?)?)?")]
    private static partial Regex WrittenDateFieldsRegex();

    [GeneratedRegex(@"[T ]\d{2}(?::?\d{2}(?::?\d{2}(?:[.,]\d+)?)?)?(?:([zZ])|([+-])(\d{2})(?::?(\d{2}))?)$")]
    private static partial Regex TimeOffsetRegex();
}
