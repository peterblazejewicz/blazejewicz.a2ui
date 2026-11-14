namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Options controlling <see cref="SurfaceManager"/> behavior when incoming A2UI messages
/// violate spec-MUST constraints.
/// </summary>
public sealed class SurfaceManagerOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), operational errors such as updating an unknown
    /// surface, updating components without a <c>root</c> component, <c>updateDataModel</c>
    /// <see cref="System.Text.Json.JsonException"/> failures (whether root snapshot replacement
    /// or nested path traversal), or creating a duplicate surface whose ID already exists throw
    /// <see cref="Messages.A2UiMessageValidationException"/> after logging the warning and raising
    /// <see cref="SurfaceManager.ErrorReported"/>. Deleting an unknown surface via
    /// <c>deleteSurface</c> also emits <c>INTEGRITY_ERROR</c> and throws
    /// <see cref="Messages.A2UiMessageValidationException"/> when <see cref="StrictMode"/> is
    /// <see langword="true"/> and <see cref="ReportErrorsOnly"/> is <see langword="false"/>.
    /// <para>
    /// When <see langword="false"/>, those operational errors are logged at <c>Warning</c> level
    /// without throwing (and <c>deleteSurface</c> on an unknown surface is treated as an idempotent
    /// no-op without raising <see cref="SurfaceManager.ErrorReported"/>) so a single bad message
    /// in a stream does not tear down the session.
    /// </para>
    /// <para>
    /// Note: structural envelope failures (<c>A2UiMessage.Validate()</c>,
    /// <c>AgentToRendererMessage.Validate()</c>, and version adapter checks), duplicate
    /// <c>v1.0</c> <c>createSurface</c> operations on an active surface (<c>EnforceUniqueness = true</c>),
    /// component composition constraint violations, and inline <c>createSurface.dataModel</c>
    /// root snapshot type errors always throw unless <see cref="ReportErrorsOnly"/> is enabled.
    /// </para>
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, operational and validation errors emit outbound
    /// <see cref="Messages.RendererError"/> messages via <see cref="SurfaceManager.ErrorReported"/>
    /// without rethrowing, matching upstream <c>processWithErrors</c> behavior, except that
    /// <c>deleteSurface</c> on an unknown <c>surfaceId</c> is treated as an idempotent no-op
    /// (neither raising <see cref="SurfaceManager.ErrorReported"/> nor throwing), per upstream
    /// conformance (<c>conformance/core/message_processor_v1_0.yaml</c>).
    /// <para>
    /// When <see langword="false"/> (default) and <see cref="StrictMode"/> is <see langword="true"/>,
    /// <c>deleteSurface</c> on an unknown <c>surfaceId</c> reports <c>INTEGRITY_ERROR</c> via
    /// <see cref="SurfaceManager.ErrorReported"/> and throws
    /// <see cref="Messages.A2UiMessageValidationException"/>.
    /// </para>
    /// </summary>
    public bool ReportErrorsOnly { get; set; }

    /// <summary>
    /// Optional set of registered catalog identifiers. When non-null, <c>createSurface</c>
    /// validates that its <c>catalogId</c> is present in this set and fails with
    /// <c>CATALOG_NOT_FOUND</c> otherwise.
    /// </summary>
    public IReadOnlySet<string>? RegisteredCatalogIds { get; set; }
}
