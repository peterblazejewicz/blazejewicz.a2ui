using System.Text.Json;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Versioning;

/// <summary>
/// Factory that inspects the "version" property of incoming JSON envelopes and returns
/// the appropriate <see cref="IVersionAdapter"/>.
/// </summary>
public static class VersionAdapterFactory
{
    private static readonly V09VersionAdapter s_v09 = new();
    private static readonly V10VersionAdapter s_v10 = new();

    /// <summary>Returns the version adapter for the specified version string.</summary>
    public static IVersionAdapter GetAdapter(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            throw new A2UiMessageValidationException("Missing required 'version' property.");
        }

        if (A2UiProtocol.IsSupportedV09Version(version))
        {
            return s_v09;
        }

        if (version == A2UiProtocol.Version10)
        {
            return s_v10;
        }

        throw new A2UiMessageValidationException($"Unsupported protocol version '{version}'.");
    }

    /// <summary>Inspects the 'version' property of a JSON element and returns the matching adapter.</summary>
    public static IVersionAdapter GetAdapter(JsonElement root)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var vEl)
            || vEl.ValueKind != JsonValueKind.String
        )
        {
            throw new A2UiMessageValidationException("Missing required 'version' property in JSON envelope.");
        }

        return GetAdapter(vEl.GetString());
    }

    /// <summary>Inspects the 'version' property of a JSON string and returns the matching adapter.</summary>
    public static IVersionAdapter GetAdapterFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return GetAdapter(doc.RootElement);
    }
}
