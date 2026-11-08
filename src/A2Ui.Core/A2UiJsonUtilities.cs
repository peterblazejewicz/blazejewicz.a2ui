using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Blazejewicz.A2Ui.Core;

/// <summary>
/// Provides centralized <see cref="JsonSerializerOptions"/> and <see cref="IJsonTypeInfoResolver"/>
/// backed by source-generated metadata (<see cref="A2UiJsonSerializerContext"/>) for A2UI protocol serialization.
/// </summary>
public static class A2UiJsonUtilities
{
    /// <summary>
    /// Gets the default <see cref="IJsonTypeInfoResolver"/> for A2UI protocol models.
    /// </summary>
    public static IJsonTypeInfoResolver DefaultTypeInfoResolver => A2UiJsonSerializerContext.Default;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured for A2UI protocol serialization and deserialization.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

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
