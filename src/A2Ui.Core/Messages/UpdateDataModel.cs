using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// Updates the data model at a JSON Pointer path.
/// If path is null or "/", replaces the entire model.
/// If value is null, deletes the key at path.
/// </summary>
public sealed record UpdateDataModel
{
    /// <summary>Identifier of the target surface.</summary>
    [JsonPropertyName("surfaceId")]
    public required string SurfaceId { get; init; }

    /// <summary>JSON Pointer path; null or "/" replaces the entire model.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>Value to set at the path; null deletes the key.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    /// <summary>Validates the updateDataModel payload against protocol rules.</summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(this.SurfaceId))
        {
            throw new A2UiMessageValidationException("Missing required 'surfaceId' property");
        }

        if (this.Value is { } val)
        {
            ValidateNoReservedKeys(val);
        }
    }

    /// <summary>Recursively validates that no JSON object key at any depth starts with 'a2ui_'.</summary>
    public static void ValidateNoReservedKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.StartsWith("a2ui_", StringComparison.Ordinal))
                {
                    throw new A2UiMessageValidationException(
                        $"Data model key '{prop.Name}' starts with reserved prefix 'a2ui_'."
                    );
                }

                ValidateNoReservedKeys(prop.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateNoReservedKeys(item);
            }
        }
    }
}
