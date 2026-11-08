using System.Text.Json;
using System.Text.Json.Serialization;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Component-level metadata conforming to common_types.json#/$defs/Extensions.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ComponentMetadata
{
    /// <summary>Optional extension metadata.</summary>
    [JsonPropertyName("extensions")]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>Validates the component metadata extensions.</summary>
    public void Validate()
    {
        if (this.Extensions is not null)
        {
            foreach (var key in this.Extensions.Keys)
            {
                Uax31Validator.ValidateExtensionKey(key);
            }
        }
    }
}
