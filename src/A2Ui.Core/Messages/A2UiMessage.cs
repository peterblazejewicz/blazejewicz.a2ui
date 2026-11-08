using System.Text.Json.Serialization;

namespace Blazejewicz.A2Ui.Core.Messages;

/// <summary>
/// A2UI v0.9 / v0.9.1 server-to-client message — one JSONL line on the wire.
/// Each message contains exactly one top-level operation key.
/// </summary>
public sealed record A2UiMessage
{
    /// <summary>Protocol version string ("v0.9" or "v0.9.1").</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Create a new surface with the given catalog and theme.</summary>
    [JsonPropertyName("createSurface")]
    public CreateSurface? CreateSurface { get; init; }

    /// <summary>Delete an existing surface by its identifier.</summary>
    [JsonPropertyName("deleteSurface")]
    public DeleteSurface? DeleteSurface { get; init; }

    /// <summary>Update the component tree on an existing surface.</summary>
    [JsonPropertyName("updateComponents")]
    public UpdateComponents? UpdateComponents { get; init; }

    /// <summary>Update the data model on an existing surface.</summary>
    [JsonPropertyName("updateDataModel")]
    public UpdateDataModel? UpdateDataModel { get; init; }

    /// <summary>
    /// Validate that this message conforms to the A2UI envelope constraints:
    /// version must be "v0.9" or "v0.9.1" and exactly one operation must be present.
    /// </summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when constraints are violated.</exception>
    public void Validate()
    {
        var violations = new List<string>();

        if (this.Version is null)
        {
            violations.Add("Missing required 'version' property");
        }
        else if (!A2UiProtocol.IsSupportedV09Version(this.Version))
        {
            violations.Add($"Unsupported version '{this.Version}'; expected 'v0.9' or 'v0.9.1'");
        }

        int opCount =
            (this.CreateSurface is not null ? 1 : 0)
            + (this.DeleteSurface is not null ? 1 : 0)
            + (this.UpdateComponents is not null ? 1 : 0)
            + (this.UpdateDataModel is not null ? 1 : 0);

        if (opCount == 0)
        {
            violations.Add(
                "Message must contain exactly one operation (createSurface, deleteSurface, updateComponents, or updateDataModel)"
            );
        }
        else if (opCount > 1)
        {
            violations.Add($"Message contains {opCount} operations; exactly one is allowed");
        }

        if (this.CreateSurface is not null && string.IsNullOrEmpty(this.CreateSurface.CatalogId))
        {
            violations.Add("CreateSurface missing required 'catalogId' in v0.9");
        }

        if (violations.Count > 0)
        {
            throw new A2UiMessageValidationException(violations);
        }
    }

    /// <summary>Returns which operation is set, or null if none/multiple.</summary>
    [JsonIgnore]
    public A2UiOperationType? Operation =>
        this switch
        {
            { CreateSurface: not null } => A2UiOperationType.CreateSurface,
            { DeleteSurface: not null } => A2UiOperationType.DeleteSurface,
            { UpdateComponents: not null } => A2UiOperationType.UpdateComponents,
            { UpdateDataModel: not null } => A2UiOperationType.UpdateDataModel,
            _ => null,
        };
}
