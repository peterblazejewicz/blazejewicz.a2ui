using System.Diagnostics;
using System.Text.Json;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;
using Blazejewicz.A2Ui.Core.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Manages the lifecycle of A2UI surfaces and their component trees.
/// Thread-safe via locking. Events are fired outside the lock to prevent
/// deadlocks if subscribers call back into GetSurface or Process.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SurfaceManager"/> class.
/// </remarks>
/// <param name="loggerFactory">Optional logger factory for structured logging.</param>
/// <param name="options">
/// Optional policy controlling how spec-MUST violations are handled. Pass
/// <c>new SurfaceManagerOptions { StrictMode = true }</c> to have the manager throw
/// <see cref="A2UiMessageValidationException"/> at each leniency site instead of
/// logging and continuing. Defaults to the lenient policy.
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

    /// <summary>
    /// Processes a v1.0 agent-to-renderer message.
    /// </summary>
    public void Process(AgentToRendererMessage message)
    {
        using var scope = DynamicValueConverter.EnterVersionScope(A2UiProtocol.Version10);
        var adapter = (V10VersionAdapter)VersionAdapterFactory.GetAdapter(A2UiProtocol.Version10);
        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(message, A2UiJsonSerializerContext.Default.AgentToRendererMessage)
        );
        var op = adapter.AdaptAgentToRenderer(doc.RootElement);
        this.Process(op);
    }

    /// <summary>
    /// Processes a raw JSON envelope string, automatically adapting between protocol versions.
    /// </summary>
    public void Process(string json)
    {
        var adapter = VersionAdapterFactory.GetAdapterFromJson(json);
        var op = adapter.AdaptAgentToRenderer(json);
        this.Process(op);
    }

    /// <summary>
    /// Processes a raw JSON element envelope, automatically adapting between protocol versions.
    /// </summary>
    public void Process(JsonElement element)
    {
        var adapter = VersionAdapterFactory.GetAdapter(element);
        var op = adapter.AdaptAgentToRenderer(element);
        this.Process(op);
    }

    /// <summary>
    /// Processes an internal operation, updating internal state and raising events.
    /// </summary>
    public void Process(InternalOperation operation)
    {
        string messageType = operation.OperationType;
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

    private void ExecuteOperationCore(InternalOperation operation)
    {
        SurfaceCreatedEventArgs? createdArgs = null;
        SurfaceDeletedEventArgs? deletedArgs = null;
        ComponentsUpdatedEventArgs? componentsArgs = null;
        DataModelUpdatedEventArgs? dataModelArgs = null;
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
                    if (this._surfaces.ContainsKey(cs.SurfaceId))
                    {
                        SurfaceManagerLog.DuplicateCreate(this._logger, cs.SurfaceId);
                        if (this._options.StrictMode || cs.EnforceUniqueness)
                        {
                            throw new A2UiMessageValidationException(
                                $"Duplicate createSurface for surface '{cs.SurfaceId}' — spec requires each createSurface to target a new surface."
                            );
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
                            var errors = CompositionValidator.Validate(cs.SurfaceId, comps, surface.ComponentConstraints);
                            if (errors.Count > 0)
                            {
                                throw new A2UiMessageValidationException(
                                    $"Composition validation failed for surface '{cs.SurfaceId}': {errors[0].Message}"
                                );
                            }
                        }

                        surface.UpdateComponents([.. comps]);
                    }
                    this._surfaces[cs.SurfaceId] = surface;
                    createdArgs = new(surface);
                    (createdPrimaryColor, createdAgentDisplayName) = ExtractThemeFields(surface.Theme);
                    break;

                case DeleteSurfaceOp ds:
                    deletedArgs = this.HandleDelete(new DeleteSurface { SurfaceId = ds.SurfaceId });
                    break;

                case UpdateComponentsOp uc:
                    componentsArgs = this.HandleUpdateComponents(
                        new UpdateComponents { SurfaceId = uc.SurfaceId, Components = [.. uc.Components] }
                    );
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
                        if (this._options.StrictMode)
                        {
                            throw new A2UiMessageValidationException(
                                $"updateDataModel references unknown surface '{ud.SurfaceId}'."
                            );
                        }
                        break;
                    }
                    try
                    {
                        s.DataModel.Apply(ud.Path, ud.Value, deleteOnNull: ud.DeleteOnNull);
                    }
                    catch (JsonException ex)
                    {
                        SurfaceManagerLog.DataModelApplyFailed(this._logger, ud.SurfaceId, ud.Path ?? "/", ex.Message);
                        if (this._options.StrictMode)
                        {
                            throw new A2UiMessageValidationException(
                                $"updateDataModel for surface '{ud.SurfaceId}' failed at path '{ud.Path ?? "/"}': {ex.Message}",
                                ex
                            );
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

    private SurfaceDeletedEventArgs? HandleDelete(DeleteSurface ds)
    {
        if (this._surfaces.Remove(ds.SurfaceId, out var surface))
        {
            return new(surface);
        }

        SurfaceManagerLog.UnknownSurfaceOp(this._logger, "deleteSurface", ds.SurfaceId);
        if (this._options.StrictMode)
        {
            throw new A2UiMessageValidationException($"deleteSurface references unknown surface '{ds.SurfaceId}'.");
        }
        return null;
    }

    private ComponentsUpdatedEventArgs? HandleUpdateComponents(UpdateComponents uc)
    {
        if (!this._surfaces.TryGetValue(uc.SurfaceId, out var surface))
        {
            SurfaceManagerLog.UnknownSurfaceOp(this._logger, "updateComponents", uc.SurfaceId);
            if (this._options.StrictMode)
            {
                throw new A2UiMessageValidationException(
                    $"updateComponents references unknown surface '{uc.SurfaceId}'."
                );
            }
            return null;
        }
        if (surface.ComponentConstraints?.Count > 0)
        {
            var errors = CompositionValidator.Validate(uc.SurfaceId, uc.Components, surface.ComponentConstraints);
            if (errors.Count > 0)
            {
                throw new A2UiMessageValidationException(
                    $"Composition validation failed for surface '{uc.SurfaceId}': {errors[0].Message}"
                );
            }
        }

        surface.UpdateComponents(uc.Components);

        // Warn if the surface still has no "root" component after this update.
        // Spec requires exactly one component with id "root".
        if (!surface.Components.ContainsKey("root"))
        {
            SurfaceManagerLog.RootComponentMissing(this._logger, uc.SurfaceId);
            if (this._options.StrictMode)
            {
                throw new A2UiMessageValidationException(
                    $"Surface '{uc.SurfaceId}' has no 'root' component after updateComponents — spec requires exactly one."
                );
            }
        }

        return new(surface, uc.Components);
    }
}
