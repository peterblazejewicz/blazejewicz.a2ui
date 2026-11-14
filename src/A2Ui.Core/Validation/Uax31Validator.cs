using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Validation;

/// <summary>
/// Validates catalog entity identifiers (components, functions, argument keys, extension keys)
/// against Unicode Standard Annex 31 (UAX 31) and protocol reservation rules.
/// </summary>
public static partial class Uax31Validator
{
    // UAX 31 identifier pattern:
    // Start: Letter, Letter Number, or underscore
    // Continue: Letter, Letter Number, Non-spacing mark, Spacing combining mark, Decimal digit, Connector punctuation, or underscore
    [GeneratedRegex(@"^[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}_]*$")]
    private static partial Regex IdentifierRegex();

    /// <summary>Returns true if the string matches the UAX 31 identifier pattern.</summary>
    public static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return IdentifierRegex().IsMatch(name);
    }

    /// <summary>Validates that the given identifier adheres to UAX 31 syntax.</summary>
    /// <exception cref="A2UiMessageValidationException">Thrown if invalid.</exception>
    public static void ValidateIdentifier(string? name, string fieldName = "Identifier")
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new A2UiMessageValidationException($"{fieldName} cannot be empty.");
        }

        if (!IsValidIdentifier(name))
        {
            throw new A2UiMessageValidationException(
                $"{fieldName} '{name}' does not conform to UAX 31 identifier syntax."
            );
        }
    }

    /// <summary>
    /// Validates a catalog-defined component name.
    /// Conforms to UAX 31 and rejects the reserved keyword "Surface".
    /// </summary>
    public static void ValidateComponentName(string? componentName)
    {
        ValidateIdentifier(componentName, "Component name");

        if (string.Equals(componentName, "Surface", StringComparison.Ordinal))
        {
            throw new A2UiMessageValidationException(
                "Component name 'Surface' is reserved and cannot be defined as a catalog component."
            );
        }
    }

    private static readonly FrozenSet<string> s_officialExtensions = FrozenSet.ToFrozenSet(
        [
            "a2ui_telemetry",
            "a2ui_experimental",
            "a2ui_theme",
        ],
        StringComparer.Ordinal
    );

    /// <summary>
    /// Validates a metadata extension key.
    /// Conforms to UAX 31 and rejects custom keys prefixed with "a2ui_".
    /// </summary>
    public static void ValidateExtensionKey(string? key)
    {
        ValidateIdentifier(key, "Extension key");

        if (key!.StartsWith("a2ui_", StringComparison.Ordinal) && !s_officialExtensions.Contains(key))
        {
            throw new A2UiMessageValidationException($"Extension key '{key}' starts with reserved prefix 'a2ui_'.");
        }
    }
}
