using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Rpc;

/// <summary>
/// Metadata and optional execution logic for a catalog function definition.
/// Maps to specification/v1_0/json/catalog_definition.json#/$defs/FunctionDefinition.
/// </summary>
public sealed class FunctionDefinition
{
    /// <summary>Function name conforming to UAX 31 identifier rules.</summary>
    public required string Name { get; init; }

    /// <summary>Catalog identifier declaring this function.</summary>
    public string? CatalogId { get; init; }

    /// <summary>Expected return type (e.g. "string", "number", "boolean", "array", "object", "validationResult", "any", "void").</summary>
    public string? ReturnType { get; init; }

    /// <summary>Allowed execution callers for this function.</summary>
    public AllowedCallers AllowedCallers { get; init; } = AllowedCallers.RendererOnly;

    /// <summary>
    /// Whether user activation/interaction is required to trigger this function (e.g., openUrl).
    /// If true, <see cref="AllowedCallers"/> must be <see cref="AllowedCallers.RendererOnly"/>.
    /// </summary>
    public bool RequiresUserActivation { get; init; }

    /// <summary>
    /// Optional local implementation delegate executing pre-resolved string arguments.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string?>, string?>? Implementation { get; init; }

    /// <summary>Validates function definition constraints.</summary>
    /// <exception cref="A2UiMessageValidationException">Thrown when constraints are violated.</exception>
    public void Validate()
    {
        if (this.RequiresUserActivation && this.AllowedCallers != AllowedCallers.RendererOnly)
        {
            throw new A2UiMessageValidationException(
                $"Function '{this.Name}': RequiresUserActivation can only be true when AllowedCallers is RendererOnly."
            );
        }
    }
}
