namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Options controlling <see cref="SurfaceManager"/> behavior when incoming A2UI messages
/// violate spec-MUST constraints.
/// </summary>
public sealed class SurfaceManagerOptions
{
    /// <summary>
    /// When <see langword="false"/> (default), operational errors such as updating or
    /// deleting an unknown surface, or creating a surface whose ID already exists, are
    /// logged at <c>Warning</c> level and ignored so a single bad message in a stream
    /// does not tear down the session.
    /// <para>
    /// When <see langword="true"/>, those same conditions throw
    /// <see cref="Messages.A2UiMessageValidationException"/> after logging the warning.
    /// <c>LoggerMessage</c> events still fire in strict mode so observability pipelines
    /// see the failure regardless of how the caller handles the exception.
    /// </para>
    /// <para>
    /// Note: structural envelope failures (<c>A2UiMessage.Validate()</c>) and root
    /// data-model snapshot type errors always throw regardless of this flag, because
    /// continuing after a malformed envelope would corrupt state.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> (lenient mode). Set to <see langword="true"/> in
    /// tests, conformance harnesses, or callers that want fail-fast semantics.
    /// </remarks>
    public bool StrictMode { get; set; }
}
