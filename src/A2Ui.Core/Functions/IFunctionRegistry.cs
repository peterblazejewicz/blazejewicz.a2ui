namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Evaluates named functions from A2UI catalog definitions.
/// Used by renderers, data-context resolvers, and <c>RpcHandler</c> to resolve <c>FunctionCall</c> values.
/// </summary>
public interface IFunctionRegistry
{
    /// <summary>
    /// Evaluate a function by name with pre-resolved string arguments.
    /// Returns null if the function is unknown or evaluation fails.
    /// </summary>
    string? Evaluate(string functionName, IReadOnlyDictionary<string, string?> resolvedArgs);

    /// <summary>
    /// Evaluate a function by name with pre-resolved string arguments and an optional evaluation context.
    /// </summary>
    string? Evaluate(
        string functionName,
        IReadOnlyDictionary<string, string?> resolvedArgs,
        FunctionEvaluationContext? context
    ) => this.Evaluate(functionName, resolvedArgs);

    /// <summary>
    /// Checks whether a function with the specified name is registered in this registry.
    /// </summary>
    bool IsRegistered(string functionName) => false;
}
