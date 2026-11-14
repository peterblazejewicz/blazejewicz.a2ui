using System.Globalization;

namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Identifies the caller role when evaluating an A2UI catalog function.
/// </summary>
public enum FunctionCallerRole
{
    /// <summary>Passive component property binding (e.g., Text.Text).</summary>
    RendererPropertyBinding,

    /// <summary>Validation check condition evaluation (<c>CheckRule.Condition</c>).</summary>
    RendererCheck,

    /// <summary>Active user-activated gesture (e.g., Button action).</summary>
    RendererAction,

    /// <summary>Remote agent invocation via <c>callRendererFunction</c>.</summary>
    AgentRpc,
}

/// <summary>
/// Abstraction for launching validated URLs on behalf of the <c>openUrl</c> function.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>
    /// Attempts to open the specified validated absolute URL.
    /// </summary>
    /// <param name="url">The validated absolute URL string (scheme is guaranteed to be http, https, mailto, or tel).</param>
    /// <returns><see langword="true"/> if the URL was handled; otherwise <see langword="false"/>.</returns>
    bool TryLaunchUrl(string url);
}

/// <summary>
/// Contextual state and host services supplied during function evaluation.
/// </summary>
public sealed record FunctionEvaluationContext
{
    /// <summary>Default evaluation context for passive property bindings.</summary>
    public static FunctionEvaluationContext Default { get; } = new();

    /// <summary>Role of the caller invoking the function.</summary>
    public FunctionCallerRole CallerRole { get; init; } = FunctionCallerRole.RendererPropertyBinding;

    /// <summary>Whether the invocation originated from an active user gesture.</summary>
    public bool IsUserActivated => this.CallerRole == FunctionCallerRole.RendererAction;

    /// <summary>Optional URL launcher for <c>openUrl</c> invocations.</summary>
    public IUrlLauncher? UrlLauncher { get; init; }

    /// <summary>Optional culture used for locale-sensitive formatting.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Optional 0-based collection template index.</summary>
    public int? CollectionIndex { get; init; }

    /// <summary>Optional protocol version (e.g. "1.0", "v0.9.1", "v0.9").</summary>
    public string? ProtocolVersion { get; init; }
}
