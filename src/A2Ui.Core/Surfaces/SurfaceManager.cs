using System.Diagnostics;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;
using Blazejewicz.A2Ui.Core.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Manages the lifecycle of A2UI surfaces and their component trees.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParseAndAdapt(string)"/>, <see cref="ParseAndAdapt(JsonElement)"/>, and
/// <see cref="ParseAndAdapt(ReadOnlySpan{byte})"/> are stateless and thread-safe for concurrent execution on
/// background or worker threads.
/// </para>
/// <para>
/// Although <see cref="SurfaceManager"/> synchronizes its internal surface registry and fires lifecycle events
/// outside the lock, <see cref="Surface"/> and <see cref="DataModel"/> instances are not thread-safe. In UI hosts,
/// <see cref="Process(InternalOperation)"/> (and its convenience overloads) as well as all <see cref="Surface"/>
/// and <see cref="DataModel"/> mutations and reads must be serialized onto the owning host/UI thread
/// (for example, Avalonia <c>Dispatcher.UIThread</c> or Blazor <c>InvokeAsync</c>).
/// </para>
/// </remarks>
/// <param name="loggerFactory">Optional logger factory for structured logging.</param>
/// <param name="options">
/// Optional policy controlling how spec-MUST violations are handled. Defaults to
/// strict upstream validation (<see cref="SurfaceManagerOptions.StrictMode"/> = <see langword="true"/>).
/// </param>
public sealed class SurfaceManager(ILoggerFactory? loggerFactory = null, SurfaceManagerOptions? options = null)
{
    private readonly Dictionary<string, Surface> _surfaces = [];

    // Use System.Threading.Lock on .NET 9+ and object monitor on .NET 8.
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif
    private readonly ILogger<SurfaceManager> _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SurfaceManager>();
    private readonly SurfaceManagerOptions _options = options ?? new SurfaceManagerOptions();

    /// <summary>Raised when a new surface is created.</summary>
    public event EventHandler<SurfaceCreatedEventArgs>? SurfaceCreated;

    /// <summary>Raised when a surface is deleted.</summary>
    public event EventHandler<SurfaceDeletedEventArgs>? SurfaceDeleted;

    /// <summary>Raised when components are updated on a surface.</summary>
    public event EventHandler<ComponentsUpdatedEventArgs>? ComponentsUpdated;

    /// <summary>Raised when the data model is updated on a surface.</summary>
    public event EventHandler<DataModelUpdatedEventArgs>? DataModelUpdated;

    /// <summary>Raised when an inbound callRendererFunction message is received.</summary>
    public event EventHandler<CallRendererFunctionOp>? CallRendererFunctionReceived;

    /// <summary>Raised when an inbound agentFunctionResponse message is received.</summary>
    public event EventHandler<AgentFunctionResponseOp>? AgentFunctionResponseReceived;

    /// <summary>Raised when a protocol or operational error occurs during message processing.</summary>
    public event EventHandler<RendererToAgentMessage>? ErrorReported;

    /// <summary>
    /// Statelessly parses, validates, and adapts a raw JSON envelope string into a version-neutral
    /// <see cref="InternalOperation"/>. Safe to call concurrently from background worker threads.
    /// </summary>
    /// <param name="json">The raw JSON envelope string.</param>
    /// <returns>The validated, version-neutral <see cref="InternalOperation"/>.</returns>
    public static InternalOperation ParseAndAdapt(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        return ParseAndAdapt(doc.RootElement);
    }

    /// <summary>
    /// Statelessly validates and adapts a JSON element envelope into a version-neutral
    /// <see cref="InternalOperation"/>. Safe to call concurrently from background worker threads.
    /// </summary>
    /// <param name="element">The root JSON element of the envelope.</param>
    /// <returns>The validated, version-neutral <see cref="InternalOperation"/>.</returns>
    public static InternalOperation ParseAndAdapt(JsonElement element)
    {
        IVersionAdapter adapter = VersionAdapterFactory.GetAdapter(element);
        return adapter.AdaptAgentToRenderer(element);
    }

    /// <summary>
    /// Statelessly parses, validates, and adapts a UTF-8 JSON envelope span into a version-neutral
    /// <see cref="InternalOperation"/>. Safe to call concurrently from background worker threads.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 encoded JSON envelope bytes.</param>
    /// <returns>The validated, version-neutral <see cref="InternalOperation"/>.</returns>
    public static InternalOperation ParseAndAdapt(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        using var doc = JsonDocument.ParseValue(ref reader);
        if (reader.Read())
        {
            throw new JsonException("Expected end of JSON after single A2UI envelope.");
        }

        return ParseAndAdapt(doc.RootElement);
    }

    /// <summary>
    /// Processes a v1.0 agent-to-renderer message.
    /// </summary>
    public void Process(AgentToRendererMessage message)
    {
        var adapter = (V10VersionAdapter)VersionAdapterFactory.GetAdapter(A2UiProtocol.Version10);
        var op = adapter.AdaptAgentToRenderer(message);
        this.Process(op);
    }

    /// <summary>
    /// Processes a raw JSON envelope string, automatically adapting between protocol versions.
    /// </summary>
    public void Process(string json) => this.Process(ParseAndAdapt(json));

    /// <summary>
    /// Processes a raw JSON element envelope, automatically adapting between protocol versions.
    /// </summary>
    public void Process(JsonElement element) => this.Process(ParseAndAdapt(element));

    /// <summary>
    /// Processes an internal operation, updating internal state and raising events.
    /// </summary>
    public void Process(InternalOperation operation)
    {
        string messageType = GetSpanOperationName(operation);
        string surfaceId = operation.TargetSurfaceId ?? "(surface-independent)";
        SurfaceManagerLog.MessageDispatched(this._logger, messageType, surfaceId);

        using Activity? activity = Diagnostics.Source.StartActivity($"Surface.{messageType}", ActivityKind.Internal);
        activity?.SetTag("a2ui.surface_id", surfaceId);
        activity?.SetTag("a2ui.message_type", messageType);

        using IDisposable? logScope = this._logger.BeginScope(
            new Dictionary<string, object> { ["SurfaceId"] = surfaceId, ["MessageType"] = messageType }
        );

        this.ExecuteOperationCore(operation);
    }

    private static string GetSpanOperationName(InternalOperation operation) =>
        operation switch
        {
            CreateSurfaceOp => "CreateSurface",
            UpdateComponentsOp => "UpdateComponents",
            UpdateDataModelOp => "UpdateDataModel",
            DeleteSurfaceOp => "DeleteSurface",
            CallRendererFunctionOp => "CallRendererFunction",
            AgentFunctionResponseOp => "AgentFunctionResponse",
            _ => operation.OperationType,
        };

    private void ExecuteOperationCore(InternalOperation operation)
    {
        SurfaceCreatedEventArgs? createdArgs = null;
        SurfaceDeletedEventArgs? deletedArgs = null;
        ComponentsUpdatedEventArgs? componentsArgs = null;
        DataModelUpdatedEventArgs? dataModelArgs = null;
        RendererToAgentMessage? reportedError = null;
        A2UiMessageValidationException? pendingException = null;
        string createdPrimaryColor = "(default)";
        string createdAgentDisplayName = "(default)";
        string componentsRootId = "(none)";
        string componentsRootType = "(none)";
        int dataModelPathCount = 0;
        string dataModelTopLevelKeys = string.Empty;

        lock (this._lock)
        {
            switch (operation)
            {
                case CreateSurfaceOp cs:
                    if (this._surfaces.TryGetValue(cs.SurfaceId, out var existingSurface))
                    {
                        SurfaceManagerLog.DuplicateCreate(this._logger, cs.SurfaceId);
                        string msg = $"Duplicate createSurface for surface '{cs.SurfaceId}' — surface already exists.";
                        reportedError = BuildRendererError(
                            "INTEGRITY_ERROR",
                            cs.SurfaceId,
                            msg,
                            version: existingSurface.ProtocolVersion ?? cs.ProtocolVersion
                        );
                        if (!this._options.ReportErrorsOnly && (this._options.StrictMode || cs.EnforceUniqueness))
                        {
                            pendingException = new A2UiMessageValidationException(msg);
                        }
                        break;
                    }

                    if (
                        this._options.RegisteredCatalogIds is { } regCats
                        && !string.IsNullOrEmpty(cs.CatalogId)
                        && !regCats.Contains(cs.CatalogId)
                    )
                    {
                        string msg = $"Catalog not found: '{cs.CatalogId}' for surface '{cs.SurfaceId}'.";
                        reportedError = BuildRendererError(
                            "CATALOG_NOT_FOUND",
                            cs.SurfaceId,
                            msg,
                            version: cs.ProtocolVersion
                        );
                        if (!this._options.ReportErrorsOnly)
                        {
                            pendingException = new A2UiMessageValidationException(msg);
                        }
                        break;
                    }

                    var surface = new Surface(cs.SurfaceId, cs.CatalogId)
                    {
                        Theme = cs.Theme,
                        SendDataModel = cs.SendDataModel,
                        Metadata = cs.Metadata,
                        SupportedCatalogIds = cs.SupportedCatalogIds,
                        ProtocolVersion = cs.ProtocolVersion,
                    };
                    if (cs.DataModel is { } dm)
                    {
                        surface.DataModel.SetSnapshot(dm);
                    }
                    if (cs.Components is { } comps && comps.Count > 0)
                    {
                        if (surface.ComponentConstraints?.Count > 0)
                        {
                            var errors = CompositionValidator.Validate(
                                cs.SurfaceId,
                                comps,
                                surface.ComponentConstraints
                            );
                            if (errors.Count > 0)
                            {
                                string msg =
                                    $"Composition validation failed for surface '{cs.SurfaceId}': {errors[0].Message}";
                                reportedError = BuildRendererError(
                                    errors[0].Code,
                                    cs.SurfaceId,
                                    msg,
                                    errors[0].Path,
                                    version: cs.ProtocolVersion
                                );
                                if (!this._options.ReportErrorsOnly)
                                {
                                    pendingException = new A2UiMessageValidationException(msg);
                                }
                                break;
                            }
                        }

                        surface.UpdateComponents([.. comps]);
                    }
                    this._surfaces[cs.SurfaceId] = surface;
                    Diagnostics.ActiveSurfaces.Add(1);
                    createdArgs = new(surface);
                    (createdPrimaryColor, createdAgentDisplayName) = ExtractThemeFields(surface.Theme);
                    break;

                case DeleteSurfaceOp ds:
                    deletedArgs = this.HandleDelete(ds, out reportedError, out pendingException);
                    break;

                case UpdateComponentsOp uc:
                    componentsArgs = this.HandleUpdateComponents(uc, out reportedError, out pendingException);
                    if (
                        componentsArgs is not null
                        && componentsArgs.Surface.Components.TryGetValue("root", out var root)
                    )
                    {
                        componentsRootId = root.Id;
                        componentsRootType = root.Component;
                    }
                    break;

                case UpdateDataModelOp ud:
                    dataModelPathCount =
                        ud.Path?.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
                    if (!this._surfaces.TryGetValue(ud.SurfaceId, out var s))
                    {
                        SurfaceManagerLog.UnknownSurfaceOp(this._logger, "updateDataModel", ud.SurfaceId);
                        string msg = $"updateDataModel references unknown surface '{ud.SurfaceId}'.";
                        reportedError = BuildRendererError(
                            "INTEGRITY_ERROR",
                            ud.SurfaceId,
                            msg,
                            version: ud.ProtocolVersion
                        );
                        if (!this._options.ReportErrorsOnly && this._options.StrictMode)
                        {
                            pendingException = new A2UiMessageValidationException(msg);
                        }
                        break;
                    }
                    try
                    {
                        s.DataModel.Apply(ud.Path, ud.Value, deleteOnNull: ud.DeleteOnNull);
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException)
                    {
                        SurfaceManagerLog.DataModelApplyFailed(this._logger, ud.SurfaceId, ud.Path ?? "/", ex.Message);
                        string msg =
                            $"updateDataModel for surface '{ud.SurfaceId}' failed at path '{ud.Path ?? "/"}': {ex.Message}";
                        reportedError = BuildRendererError(
                            "DATA_ERROR",
                            ud.SurfaceId,
                            msg,
                            ud.Path,
                            version: s.ProtocolVersion ?? ud.ProtocolVersion
                        );
                        if (!this._options.ReportErrorsOnly && this._options.StrictMode)
                        {
                            pendingException = new A2UiMessageValidationException(msg, ex);
                        }
                        break;
                    }
                    dataModelArgs = new(s);
                    dataModelTopLevelKeys = string.Join(",", s.DataModel.TopLevelKeys);
                    break;

                case CallRendererFunctionOp:
                    break;

                case AgentFunctionResponseOp:
                    break;
            }
        }

        Diagnostics.SurfaceOperations.Add(
            1,
            new KeyValuePair<string, object?>("a2ui.operation.type", operation.OperationType),
            new KeyValuePair<string, object?>("a2ui.status", reportedError is null ? "ok" : "error")
        );

        if (reportedError?.Error is { } rendererError)
        {
            Diagnostics.Errors.Add(
                1,
                new KeyValuePair<string, object?>("a2ui.error.code", rendererError.Code)
            );
        }

        if (reportedError is not null)
        {
            this.ErrorReported?.Invoke(this, reportedError);
        }

        if (pendingException is not null)
        {
            throw pendingException;
        }

        if (createdArgs is not null)
        {
            SurfaceManagerLog.SurfaceCreated(
                this._logger,
                createdArgs.Surface.SurfaceId,
                createdArgs.Surface.CatalogId ?? "(none)",
                createdPrimaryColor,
                createdAgentDisplayName
            );
            this.SurfaceCreated?.Invoke(this, createdArgs);
        }
        if (deletedArgs is not null)
        {
            SurfaceManagerLog.SurfaceDeleted(this._logger, deletedArgs.Surface.SurfaceId);
            this.SurfaceDeleted?.Invoke(this, deletedArgs);
        }
        if (componentsArgs is not null)
        {
            SurfaceManagerLog.ComponentsUpdated(
                this._logger,
                componentsArgs.Surface.SurfaceId,
                componentsArgs.Updated.Length,
                componentsRootId,
                componentsRootType
            );
            this.ComponentsUpdated?.Invoke(this, componentsArgs);
        }
        if (dataModelArgs is not null)
        {
            SurfaceManagerLog.DataModelUpdated(
                this._logger,
                dataModelArgs.Surface.SurfaceId,
                dataModelPathCount,
                dataModelTopLevelKeys
            );
            this.DataModelUpdated?.Invoke(this, dataModelArgs);
        }
        if (operation is CallRendererFunctionOp crfOp)
        {
            this.CallRendererFunctionReceived?.Invoke(this, crfOp);
        }
        if (operation is AgentFunctionResponseOp afrOp)
        {
            this.AgentFunctionResponseReceived?.Invoke(this, afrOp);
        }
    }

    private static RendererToAgentMessage BuildRendererError(
        string code,
        string surfaceId,
        string message,
        string? path = null,
        string? version = null
    ) =>
        new()
        {
            Version = version ?? A2UiProtocol.Version10,
            Error = new RendererError
            {
                Code = code,
                SurfaceId = surfaceId,
                Path = path,
                Message = message,
            },
        };

    /// <summary>
    /// Processes a single A2UI message, updating internal state and raising events.
    /// </summary>
    /// <param name="message">The validated A2UI message to process.</param>
    /// <exception cref="A2UiMessageValidationException">Thrown when the message is invalid.</exception>
    public void Process(A2UiMessage message)
    {
        string messageType = message.Operation?.ToString() ?? "(empty)";
        string surfaceId =
            message.CreateSurface?.SurfaceId
            ?? message.DeleteSurface?.SurfaceId
            ?? message.UpdateComponents?.SurfaceId
            ?? message.UpdateDataModel?.SurfaceId
            ?? "(unknown)";
        SurfaceManagerLog.MessageDispatched(this._logger, messageType, surfaceId);

        using Activity? activity = Diagnostics.Source.StartActivity($"Surface.{messageType}", ActivityKind.Internal);
        activity?.SetTag("a2ui.surface_id", surfaceId);
        activity?.SetTag("a2ui.message_type", messageType);

        using IDisposable? logScope = this._logger.BeginScope(
            new Dictionary<string, object> { ["SurfaceId"] = surfaceId, ["MessageType"] = messageType }
        );

        try
        {
            message.Validate();
        }
        catch (A2UiMessageValidationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            SurfaceManagerLog.ValidationFailed(this._logger, messageType, ex.Message);
            throw;
        }

        var adapter = (V09VersionAdapter)VersionAdapterFactory.GetAdapter(A2UiProtocol.LatestV09);
        var op = adapter.AdaptAgentToRenderer(message);
        this.ExecuteOperationCore(op);
    }

    /// <summary>
    /// Extracts theme fields from the catalog theme object for telemetry.
    /// Spec schema (basic_catalog.json #/$defs/theme) defines primaryColor,
    /// iconUrl, and agentDisplayName. We surface primaryColor and
    /// agentDisplayName as they're the most human-meaningful for run comparison.
    /// </summary>
    private static (string PrimaryColor, string AgentDisplayName) ExtractThemeFields(JsonElement? theme)
    {
        if (theme is null || theme.Value.ValueKind != JsonValueKind.Object)
        {
            return ("(default)", "(default)");
        }

        string color =
            theme.Value.TryGetProperty("primaryColor", out var c) && c.ValueKind == JsonValueKind.String
                ? (c.GetString() ?? "(default)")
                : "(default)";
        string agent =
            theme.Value.TryGetProperty("agentDisplayName", out var a) && a.ValueKind == JsonValueKind.String
                ? (a.GetString() ?? "(default)")
                : "(default)";
        return (color, agent);
    }

    /// <summary>
    /// Removes all tracked surfaces and fires <see cref="SurfaceDeleted"/> for each.
    /// Used when a new prompt is sent and old surfaces should be discarded.
    /// </summary>
    public void Clear()
    {
        List<Surface> cleared;
        lock (this._lock)
        {
            cleared = [.. this._surfaces.Values];
            this._surfaces.Clear();
        }

        foreach (var surface in cleared)
        {
            SurfaceManagerLog.SurfaceDeleted(this._logger, surface.SurfaceId);
            SurfaceDeleted?.Invoke(this, new SurfaceDeletedEventArgs(surface));
        }

        if (cleared.Count > 0)
        {
            Diagnostics.ActiveSurfaces.Add(-cleared.Count);
            SurfaceManagerLog.SurfacesCleared(this._logger, cleared.Count);
        }
    }

    /// <summary>Gets the surface with the specified identifier, or <see langword="null"/> if not found.</summary>
    /// <param name="surfaceId">The surface identifier to look up.</param>
    /// <returns>The surface, or <see langword="null"/>.</returns>
    public Surface? GetSurface(string surfaceId)
    {
        lock (this._lock)
        {
            return this._surfaces.GetValueOrDefault(surfaceId);
        }
    }

    /// <summary>Gets a snapshot of all currently active surfaces.</summary>
    /// <returns>A read-only list of active surfaces.</returns>
    public IReadOnlyList<Surface> GetSurfaces()
    {
        lock (this._lock)
        {
            return [.. this._surfaces.Values];
        }
    }

    private SurfaceDeletedEventArgs? HandleDelete(
        DeleteSurfaceOp ds,
        out RendererToAgentMessage? reportedError,
        out A2UiMessageValidationException? pendingException
    )
    {
        reportedError = null;
        pendingException = null;

        if (this._surfaces.Remove(ds.SurfaceId, out var surface))
        {
            Diagnostics.ActiveSurfaces.Add(-1);
            return new(surface);
        }

        SurfaceManagerLog.UnknownSurfaceOp(this._logger, "deleteSurface", ds.SurfaceId);
        if (!this._options.ReportErrorsOnly && this._options.StrictMode)
        {
            string msg = $"deleteSurface references unknown surface '{ds.SurfaceId}'.";
            reportedError = BuildRendererError("INTEGRITY_ERROR", ds.SurfaceId, msg, version: ds.ProtocolVersion);
            pendingException = new A2UiMessageValidationException(msg);
        }
        return null;
    }

    private ComponentsUpdatedEventArgs? HandleUpdateComponents(
        UpdateComponentsOp uc,
        out RendererToAgentMessage? reportedError,
        out A2UiMessageValidationException? pendingException
    )
    {
        reportedError = null;
        pendingException = null;

        if (!this._surfaces.TryGetValue(uc.SurfaceId, out var surface))
        {
            SurfaceManagerLog.UnknownSurfaceOp(this._logger, "updateComponents", uc.SurfaceId);
            string msg = $"updateComponents references unknown surface '{uc.SurfaceId}'.";
            reportedError = BuildRendererError("INTEGRITY_ERROR", uc.SurfaceId, msg, version: uc.ProtocolVersion);
            if (!this._options.ReportErrorsOnly && this._options.StrictMode)
            {
                pendingException = new A2UiMessageValidationException(msg);
            }
            return null;
        }

        if (surface.ComponentConstraints?.Count > 0)
        {
            // Merge existing surface components with incoming batch so cross-batch composition constraints are validated.
            var mergedById = new Dictionary<string, A2UiComponent>(surface.Components, StringComparer.Ordinal);
            foreach (var c in uc.Components)
            {
                mergedById[c.Id] = c;
            }

            var errors = CompositionValidator.Validate(
                uc.SurfaceId,
                [.. mergedById.Values],
                surface.ComponentConstraints
            );
            if (errors.Count > 0)
            {
                string msg = $"Composition validation failed for surface '{uc.SurfaceId}': {errors[0].Message}";
                reportedError = BuildRendererError(
                    errors[0].Code,
                    uc.SurfaceId,
                    msg,
                    errors[0].Path,
                    version: surface.ProtocolVersion ?? uc.ProtocolVersion
                );
                if (!this._options.ReportErrorsOnly)
                {
                    pendingException = new A2UiMessageValidationException(msg);
                }
                return null;
            }
        }

        A2UiComponent[] updated = uc.Components as A2UiComponent[] ?? [.. uc.Components];
        surface.UpdateComponents(updated);

        // Warn if the surface still has no "root" component after this update.
        // Spec requires exactly one component with id "root".
        if (!surface.Components.ContainsKey("root"))
        {
            SurfaceManagerLog.RootComponentMissing(this._logger, uc.SurfaceId);
            string msg =
                $"Surface '{uc.SurfaceId}' has no 'root' component after updateComponents — spec requires exactly one.";
            reportedError = BuildRendererError(
                "INTEGRITY_ERROR",
                uc.SurfaceId,
                msg,
                version: surface.ProtocolVersion ?? uc.ProtocolVersion
            );
            if (!this._options.ReportErrorsOnly && this._options.StrictMode)
            {
                pendingException = new A2UiMessageValidationException(msg);
            }
        }

        return new(surface, updated);
    }
}
