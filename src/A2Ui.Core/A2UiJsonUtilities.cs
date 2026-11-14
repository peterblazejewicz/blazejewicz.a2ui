using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Provides centralized <see cref="JsonSerializerOptions"/> and <see cref="IJsonTypeInfoResolver"/>
/// backed by source-generated metadata (<see cref="A2UiJsonSerializerContext"/>) for A2UI protocol serialization.
/// </summary>
public static class A2UiJsonUtilities
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, string> s_optionsToVersion = new();

    /// <summary>
    /// Gets the default <see cref="IJsonTypeInfoResolver"/> for A2UI protocol models.
    /// </summary>
    public static IJsonTypeInfoResolver DefaultTypeInfoResolver => A2UiJsonSerializerContext.Default;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured for A2UI protocol serialization and deserialization.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Gets the source-generated <see cref="A2UiJsonSerializerContext"/> bound to A2UI v0.9 / v0.9.1 protocol semantics.
    /// </summary>
    public static A2UiJsonSerializerContext V09Context { get; } = CreateContext(A2UiProtocol.Version091);

    /// <summary>
    /// Gets the source-generated <see cref="A2UiJsonSerializerContext"/> bound to A2UI v1.0 protocol semantics.
    /// </summary>
    public static A2UiJsonSerializerContext V10Context { get; } = CreateContext(A2UiProtocol.Version10);

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> bound to A2UI v0.9 / v0.9.1 protocol semantics.
    /// </summary>
    public static JsonSerializerOptions V09Options => V09Context.Options;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> bound to A2UI v1.0 protocol semantics.
    /// </summary>
    public static JsonSerializerOptions V10Options => V10Context.Options;

    /// <summary>
    /// Creates a new read-only <see cref="JsonSerializerOptions"/> bound to the specified A2UI protocol version.
    /// </summary>
    /// <param name="protocolVersion">The protocol version (for example, <see cref="A2UiProtocol.Version10"/> or <see cref="A2UiProtocol.Version091"/>).</param>
    /// <returns>A read-only <see cref="JsonSerializerOptions"/> configured for <paramref name="protocolVersion"/>.</returns>
    public static JsonSerializerOptions CreateOptions(string protocolVersion) => CreateContext(protocolVersion).Options;

    /// <summary>
    /// Gets the A2UI protocol version associated with <paramref name="options"/>, or <see langword="null"/> if none is registered.
    /// </summary>
    /// <param name="options">The serializer options to inspect.</param>
    /// <returns>The registered protocol version string, or <see langword="null"/>.</returns>
    public static string? GetProtocolVersion(JsonSerializerOptions? options) =>
        options is not null && s_optionsToVersion.TryGetValue(options, out string? version) ? version : null;

    private static A2UiJsonSerializerContext CreateContext(string protocolVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(protocolVersion);

        var options = new JsonSerializerOptions(A2UiJsonSerializerContext.Default.Options)
        {
            AllowOutOfOrderMetadataProperties = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = null,
        };
        s_optionsToVersion.AddOrUpdate(options, protocolVersion);
        var context = new A2UiJsonSerializerContext(options);
        if (!ReferenceEquals(context.Options, options))
        {
            s_optionsToVersion.AddOrUpdate(context.Options, protocolVersion);
        }

        return context;
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions(A2UiJsonSerializerContext.Default.Options)
        {
            AllowOutOfOrderMetadataProperties = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = A2UiJsonSerializerContext.Default,
        };
        options.MakeReadOnly();
        return options;
    }
}
