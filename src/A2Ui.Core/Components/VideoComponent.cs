using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Bindings;

namespace Blazejewicz.A2Ui.Core.Components;

/// <summary>Video placeholder component.</summary>
public sealed record VideoComponent : A2UiComponent
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Component { get; init; } = "Video";

    /// <summary>Video source URL.</summary>
    [JsonPropertyName("url")]
    public DynamicValue? Url { get; init; }

    /// <summary>Poster image URL displayed before the video plays (v1.0).</summary>
    [JsonPropertyName("posterUrl")]
    public DynamicValue? PosterUrl { get; init; }
}
