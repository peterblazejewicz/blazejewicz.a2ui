using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Components;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>Operation payload for updating components on a surface.</summary>
public sealed record UpdateComponents
{
    /// <summary>Identifier of the target surface.</summary>
    [JsonPropertyName("surfaceId")]
    public required string SurfaceId { get; init; }

    /// <summary>Components to add or update in the surface's component tree.</summary>
    [JsonPropertyName("components")]
    public required A2UiComponent[] Components { get; init; }

    /// <summary>Validates the updateComponents payload.</summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(this.SurfaceId))
        {
            throw new A2UiMessageValidationException("Missing required 'surfaceId' property");
        }
    }
}
