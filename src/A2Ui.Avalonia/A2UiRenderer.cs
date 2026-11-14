using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Avalonia;

/// <summary>
/// Renders an A2UI surface to Avalonia controls.
/// Must be called on the UI thread (Avalonia Dispatcher).
/// </summary>
public sealed class A2UiRenderer(
    CatalogRegistry catalog,
    IFunctionRegistry? functionRegistry = null,
    ILoggerFactory? loggerFactory = null,
    RpcHandler? rpcHandler = null,
    A2UiAvaloniaOptions? options = null
) : IDisposable
{
    private readonly CatalogRegistry _catalog = catalog;
    private readonly IFunctionRegistry? _functionRegistry = functionRegistry;
    private readonly ILogger<A2UiRenderer> _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<A2UiRenderer>();
    private readonly Dictionary<string, WeakReference<Surface>> _surfaceInstances = [];
    private readonly Dictionary<string, Dictionary<string, Control>> _surfaceCaches = [];
    private readonly Dictionary<string, SurfaceRpcCache> _rpcCaches = [];
    private readonly Dictionary<string, CancellationTokenSource> _surfaceCts = [];
    private readonly A2UiAvaloniaOptions? _options = options;
    private bool _disposed;

    /// <summary>Optional RPC handler for agent-backed catalog functions.</summary>
    public RpcHandler? RpcHandler { get; internal set; } = rpcHandler;

    /// <summary>
    /// Render or update the complete surface.
    /// Returns the root control for insertion into the visual tree.
    /// </summary>
    public Control Render(Surface surface)
    {
        Dispatcher.UIThread.VerifyAccess();

        using Activity? activity = Diagnostics.RendererSource.StartActivity("Renderer.Render", ActivityKind.Internal);
        activity?.SetTag("a2ui.surface_id", surface.SurfaceId);
        activity?.SetTag("a2ui.catalog_id", surface.CatalogId);

        if (
            surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(surface.CatalogId)
            && !this._catalog.IsKnownCatalog(surface.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Surface '{surface.SurfaceId}' specifies unregistered catalog '{surface.CatalogId}'."
            );
        }

        if (
            this._surfaceInstances.TryGetValue(surface.SurfaceId, out var prevRef)
            && (!prevRef.TryGetTarget(out var prevSurface) || !ReferenceEquals(prevSurface, surface))
        )
        {
            this.ClearSurface(surface.SurfaceId);
        }

        this._surfaceInstances[surface.SurfaceId] = new WeakReference<Surface>(surface);

        if (!this._surfaceCaches.TryGetValue(surface.SurfaceId, out var cache))
        {
            cache = [];
            this._surfaceCaches[surface.SurfaceId] = cache;
        }

        if (!this._rpcCaches.TryGetValue(surface.SurfaceId, out var rpcCache))
        {
            rpcCache = new SurfaceRpcCache();
            this._rpcCaches[surface.SurfaceId] = rpcCache;
        }

        if (!this._surfaceCts.TryGetValue(surface.SurfaceId, out var cts))
        {
            cts = new CancellationTokenSource();
            this._surfaceCts[surface.SurfaceId] = cts;
        }

        var session = new RenderSession(
            surface,
            this._catalog,
            this._functionRegistry,
            (surfaceId, eventName, payload, componentId, userMessage) =>
                this.UserActionFired?.Invoke(this, new(surfaceId, eventName, payload, componentId, userMessage)),
            surfaceId => this.DataModelChanged?.Invoke(this, new(surfaceId)),
            Logger: this._logger,
            SurfaceCancellation: cts.Token,
            RpcHandler: this.RpcHandler,
            Options: this._options,
            RpcCache: rpcCache,
            OnRpcSettled: s =>
            {
                if (
                    !this._disposed
                    && this._surfaceInstances.TryGetValue(s.SurfaceId, out var currentRef)
                    && currentRef.TryGetTarget(out var currentSurface)
                    && ReferenceEquals(currentSurface, s)
                )
                {
                    _ = this.Render(s);
                    this.DataModelChanged?.Invoke(this, new(s.SurfaceId));
                }
            },
            OnRendererError: error =>
                this.ErrorReported?.Invoke(
                    this,
                    new RendererToAgentMessage
                    {
                        Version = surface.ProtocolVersion ?? A2UiProtocol.Version10,
                        Error = error,
                    }
                )
        );
        var context = new RenderContext(session, cache);

        var roots = surface.GetRootComponents().ToList();

        if (roots.Count == 1)
        {
            return this.RenderComponent(roots[0], surface, context);
        }

        var container = new StackPanel { Spacing = 8 };
        foreach (var root in roots)
        {
            var renderedRoot = this.RenderComponent(root, surface, context);
            RenderContext.DetachFromParent(renderedRoot, this._logger);
            container.Children.Add(renderedRoot);
        }

        return container;
    }

    /// <summary>Raised when a user interacts with a rendered component (e.g., button click).</summary>
    public event EventHandler<UserActionEventArgs>? UserActionFired;

    /// <summary>Raised when the data model is mutated via two-way binding (e.g., text input).</summary>
    public event EventHandler<DataModelChangedEventArgs>? DataModelChanged;

    /// <summary>Raised when a renderer error occurs during action dispatch, expression evaluation, or RPC routing.</summary>
    public event EventHandler<RendererToAgentMessage>? ErrorReported;

    private Control RenderComponent(A2UiComponent component, Surface surface, RenderContext context)
    {
        RendererLog.RenderComponent(this._logger, component.Id, component.Component, component.Parent ?? "(none)");

        if (
            surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(component.CatalogId)
            && !this._catalog.IsKnownCatalog(component.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Component '{component.Id}' specifies unregistered catalog '{component.CatalogId}'."
            );
        }

        if (!this._catalog.TryResolveEntry(surface, component, out var entry))
        {
            RendererLog.UnknownComponentTypeRendered(this._logger, component.Id, component.Component);
            return new TextBlock { Text = $"[Unknown component: {component.Component}]", Classes = { "Caption" } };
        }

        context.ActiveAncestors.Add(component.Id);
        try
        {
            var cache = this._surfaceCaches.GetValueOrDefault(surface.SurfaceId);
            if (cache is not null && cache.TryGetValue(component.Id, out var existing))
            {
                Control? previousContainer = context.UpdatingContainer;
                context.UpdatingContainer = existing;
                bool updated;
                try
                {
                    updated = entry.Update(existing, component, surface.DataModel, context);
                }
                finally
                {
                    context.UpdatingContainer = previousContainer;
                }

                if (updated)
                {
                    ApplyAccessibility(existing, component, context);
                    return existing;
                }
            }

            Control? previousForCreate = context.UpdatingContainer;
            context.UpdatingContainer = null;
            Control control;
            try
            {
                control = entry.Create(component, surface.DataModel, context);
            }
            finally
            {
                context.UpdatingContainer = previousForCreate;
            }

            ApplyAccessibility(control, component, context);
            cache?[component.Id] = control;

            return control;
        }
        finally
        {
            context.ActiveAncestors.Remove(component.Id);
        }
    }

    /// <summary>Clear control cache for a specific surface and cancel in-flight async operations.</summary>
    public void ClearSurface(string surfaceId)
    {
        this._surfaceInstances.Remove(surfaceId);
        this._surfaceCaches.Remove(surfaceId);
        this._rpcCaches.Remove(surfaceId);

        if (this._surfaceCts.Remove(surfaceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// Disposes the renderer, cancelling and disposing all per-surface cancellation tokens and clearing caches.
    /// </summary>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this._surfaceInstances.Clear();
        this._surfaceCaches.Clear();
        this._rpcCaches.Clear();
        foreach (var cts in this._surfaceCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        this._surfaceCts.Clear();
    }

    private static readonly AttachedProperty<bool> s_isExplicitlyHiddenProperty =
        AvaloniaProperty.RegisterAttached<A2UiRenderer, Control, bool>("IsExplicitlyHidden");

    internal static void ApplyAccessibility(Control control, A2UiComponent component, IRenderContext context)
    {
        var acc = component.Accessibility;

        string? label = acc?.Label is not null ? context.Resolve(acc.Label) : null;
        AutomationProperties.SetName(control, string.IsNullOrEmpty(label) ? null : label);

        string? description = acc?.Description is not null ? context.Resolve(acc.Description) : null;
        AutomationProperties.SetHelpText(control, string.IsNullOrEmpty(description) ? null : description);

        string? protocolVersion = (context as RenderContext)?.Surface.ProtocolVersion;
        bool supportsV10Accessibility =
            protocolVersion is null
            || string.Equals(protocolVersion, A2UiProtocol.Version10, StringComparison.Ordinal);

        if (!supportsV10Accessibility)
        {
            AutomationProperties.SetLiveSetting(control, AutomationLiveSetting.Off);
            control.SetValue(s_isExplicitlyHiddenProperty, false);
            PropagateSubtreeAccessibilityView(control, HasExplicitlyHiddenAncestor(control));
            return;
        }

        var setting = acc?.Live switch
        {
            "polite" => AutomationLiveSetting.Polite,
            "assertive" => AutomationLiveSetting.Assertive,
            _ => AutomationLiveSetting.Off,
        };
        AutomationProperties.SetLiveSetting(control, setting);

        bool isHidden = false;
        if (acc?.Hidden is not null)
        {
            string? hiddenStr = context.Resolve(acc.Hidden);
            if (bool.TryParse(hiddenStr, out bool parsedHidden))
            {
                isHidden = parsedHidden;
            }
        }

        control.SetValue(s_isExplicitlyHiddenProperty, isHidden);
        bool effectiveHidden = isHidden || HasExplicitlyHiddenAncestor(control);
        PropagateSubtreeAccessibilityView(control, effectiveHidden);
    }

    private static bool HasExplicitlyHiddenAncestor(Control control)
    {
        ILogical? parent = ((ILogical)control).LogicalParent;
        while (parent is not null)
        {
            if (parent is Control parentControl && parentControl.GetValue(s_isExplicitlyHiddenProperty))
            {
                return true;
            }

            parent = parent.LogicalParent;
        }

        return false;
    }

    private static void PropagateSubtreeAccessibilityView(Control control, bool effectiveHidden)
    {
        AutomationProperties.SetAccessibilityView(
            control,
            effectiveHidden ? AccessibilityView.Raw : AccessibilityView.Content
        );

        foreach (ILogical logicalChild in ((ILogical)control).LogicalChildren)
        {
            if (logicalChild is Control childControl)
            {
                bool childEffectiveHidden =
                    effectiveHidden || childControl.GetValue(s_isExplicitlyHiddenProperty);
                PropagateSubtreeAccessibilityView(childControl, childEffectiveHidden);
            }
        }
    }
}

internal sealed class SurfaceRpcCache
{
    private const int MaxEntries = 256;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (bool HasError, string? Value)> _settled = new(StringComparer.Ordinal);
    private readonly Queue<string> _fifo = new();
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);

    public bool TryGetSettled(string key, out bool hasError, out string? value)
    {
        lock (this._gate)
        {
            if (this._settled.TryGetValue(key, out var entry))
            {
                hasError = entry.HasError;
                value = entry.Value;
                return true;
            }

            hasError = false;
            value = null;
            return false;
        }
    }

    public bool TryMarkInFlight(string key)
    {
        lock (this._gate)
        {
            return this._inFlight.Add(key);
        }
    }

    public void Settle(string key, bool hasError, string? value)
    {
        lock (this._gate)
        {
            this._inFlight.Remove(key);
            if (!this._settled.ContainsKey(key))
            {
                if (this._fifo.Count >= MaxEntries)
                {
                    string oldest = this._fifo.Dequeue();
                    this._settled.Remove(oldest);
                }

                this._fifo.Enqueue(key);
            }

            this._settled[key] = (hasError, value);
        }
    }

    public void RemoveInFlight(string key)
    {
        lock (this._gate)
        {
            this._inFlight.Remove(key);
        }
    }
}

internal sealed record RenderSession(
    Surface Surface,
    CatalogRegistry Catalog,
    IFunctionRegistry? FunctionRegistry,
    Action<string, string, object?, string?, string?> FireAction,
    Action<string> OnDataModelChanged,
    ILogger? Logger = null,
    CancellationToken SurfaceCancellation = default,
    RpcHandler? RpcHandler = null,
    A2UiAvaloniaOptions? Options = null,
    SurfaceRpcCache? RpcCache = null,
    Action<Surface>? OnRpcSettled = null,
    Action<RendererError>? OnRendererError = null
);

internal sealed class RenderContext(
    RenderSession session,
    Dictionary<string, Control> cache,
    string? basePath = null,
    int? collectionIndex = null,
    HashSet<string>? activeAncestors = null
) : IRenderContext
{
    internal HashSet<string> ActiveAncestors { get; } = activeAncestors ?? [];
    internal Control? UpdatingContainer { get; set; }
    internal Surface Surface => session.Surface;
    public ILogger? Logger => session.Logger;
    public CancellationToken SurfaceCancellation => session.SurfaceCancellation;
    public int? CollectionIndex => collectionIndex;
    public RpcHandler? RpcHandler => session.RpcHandler;

    public IRenderContext WithIndex(int index) =>
        new RenderContext(session, cache, basePath, index, this.ActiveAncestors);

    private const int MaxResolveDepth = 32;
    private static readonly ExpressionParser s_expressionParser = new();

    private JsonSerializerOptions VersionOptions =>
        session.Surface.ProtocolVersion == A2UiProtocol.Version10
            ? A2UiJsonUtilities.V10Options
            : A2UiJsonUtilities.V09Options;

    public Control? RenderChild(string? childId)
    {
        if (childId is null || !session.Surface.Components.TryGetValue(childId, out var c))
        {
            return null;
        }

        if (this.ActiveAncestors.Contains(c.Id))
        {
            if (session.Logger is not null)
            {
                RendererLog.CyclicReferenceDetected(session.Logger, c.Id, session.Surface.SurfaceId);
            }

            return null;
        }

        if (
            session.Surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(c.CatalogId)
            && !session.Catalog.IsKnownCatalog(c.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Component '{c.Id}' specifies unregistered catalog '{c.CatalogId}'."
            );
        }

        if (!session.Catalog.TryResolveEntry(session.Surface, c, out var entry))
        {
            if (session.Logger is not null)
            {
                RendererLog.UnknownComponentTypeRendered(session.Logger, c.Id, c.Component);
            }

            return null;
        }

        this.ActiveAncestors.Add(c.Id);
        try
        {
            if (cache.TryGetValue(c.Id, out var existing))
            {
                Control? previousContainer = this.UpdatingContainer;
                this.UpdatingContainer = existing;
                bool updated;
                try
                {
                    updated = entry.Update(existing, c, session.Surface.DataModel, this);
                }
                finally
                {
                    this.UpdatingContainer = previousContainer;
                }

                if (updated)
                {
                    A2UiRenderer.ApplyAccessibility(existing, c, this);
                    if (this.UpdatingContainer is null || !IsDirectSlotOf(existing, this.UpdatingContainer))
                    {
                        DetachFromParent(existing, session.Logger);
                    }

                    return existing;
                }
            }

            Control? previousForCreate = this.UpdatingContainer;
            this.UpdatingContainer = null;
            Control control;
            try
            {
                control = entry.Create(c, session.Surface.DataModel, this);
            }
            finally
            {
                this.UpdatingContainer = previousForCreate;
            }

            A2UiRenderer.ApplyAccessibility(control, c, this);
            cache[c.Id] = control;
            return control;
        }
        finally
        {
            this.ActiveAncestors.Remove(c.Id);
        }
    }

    // Slot-wrapper controls (List's ScrollViewer content host, Card's Border/StackPanel host,
    // Tabs' TabControl/TabItem host) own visual children inside an intermediate wrapper rather than
    // directly under updatingContainer; recognizing them prevents DetachFromParent during in-place refresh.
    private static bool IsDirectSlotOf(Control child, Control updatingContainer)
    {
        if (ReferenceEquals(child.Parent, updatingContainer))
        {
            return true;
        }

        if (updatingContainer is ScrollViewer sv && ReferenceEquals(child.Parent, sv.Content))
        {
            return true;
        }

        if (updatingContainer is Border { Child: StackPanel cardPanel } && ReferenceEquals(child.Parent, cardPanel))
        {
            return true;
        }

        if (updatingContainer is TabControl && child.Parent is TabItem ti && ReferenceEquals(ti.Parent, updatingContainer))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detach a control from its current visual parent so it can be
    /// safely added to a new container. Handles Panel, ContentControl,
    /// and Decorator (Border) parent types.
    /// </summary>
    internal static void DetachFromParent(Control control, ILogger? logger = null)
    {
        switch (control.Parent)
        {
            case null:
                break;
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl cc when ReferenceEquals(cc.Content, control):
                cc.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
            default:
                if (logger is not null)
                {
                    RendererLog.CannotDetachFromUnknownParent(logger, control.Parent.GetType().Name);
                }

                break;
        }
    }

    /// <summary>
    /// Renders child controls for <paramref name="parentId"/> from <see cref="A2UiComponent.Children"/>
    /// (ID list or template), falling back to reverse <see cref="A2UiComponent.Parent"/> lookup.
    /// </summary>
    public IEnumerable<Control> RenderChildren(string parentId)
    {
        if (session.Surface.Components.TryGetValue(parentId, out var comp) && comp.Children is not null)
        {
            if (comp.Children.Ids is { } ids)
            {
                return ids.Select(id => this.RenderChild(id)).OfType<Control>();
            }

            if (comp.Children.Template is { } tmpl)
            {
                return this.ExpandTemplate(tmpl);
            }
        }

        // Fall back to parent-based lookup
        return session
            .Surface.Components.Values.Where(c => c.Parent == parentId)
            .Select(c => this.RenderChild(c.Id))
            .OfType<Control>();
    }

    /// <summary>
    /// Expand a template child list by iterating over the data model array
    /// at the template path and rendering the template component tree once
    /// per array item, with relative path resolution scoped to each item.
    /// </summary>
    private List<Control> ExpandTemplate(ChildTemplate tmpl)
    {
        string arrayPath = tmpl.Path.TrimStart('/');
        int count = session.Surface.DataModel.GetArrayLength(tmpl.Path);
        if (count <= 0)
        {
            return [];
        }

        if (!session.Surface.Components.TryGetValue(tmpl.ComponentId, out var templateComp))
        {
            return [];
        }

        if (
            session.Surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(templateComp.CatalogId)
            && !session.Catalog.IsKnownCatalog(templateComp.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Component '{templateComp.Id}' specifies unregistered catalog '{templateComp.CatalogId}'."
            );
        }

        if (!session.Catalog.TryResolveEntry(session.Surface, templateComp, out var entry))
        {
            return [];
        }

        var controls = new List<Control>(count);
        for (int i = 0; i < count; i++)
        {
            string itemBasePath = $"/{arrayPath}/{i}";

            // Each template instance gets its own cache so children rendered
            // from the same component IDs (e.g. rc_title) don't collide.
            var instanceCache = new Dictionary<string, Control>();
            var scopedContext = new RenderContext(
                session,
                instanceCache,
                itemBasePath,
                collectionIndex: i,
                activeAncestors: this.ActiveAncestors
            );

            var control = entry.Create(templateComp, session.Surface.DataModel, scopedContext);
            A2UiRenderer.ApplyAccessibility(control, templateComp, scopedContext);
            controls.Add(control);
        }

        if (session.Logger is not null)
        {
            RendererLog.TemplateInstantiated(session.Logger, tmpl.ComponentId, templateComp.Component, controls.Count);
        }

        return controls;
    }

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) =>
        session.FireAction(session.Surface.SurfaceId, eventName, payload, componentId, null);

    public void FireUserAction(string eventName, object? payload, string? componentId, string? userMessage) =>
        session.FireAction(session.Surface.SurfaceId, eventName, payload, componentId, userMessage);

    public string? Resolve(DynamicValue? value) =>
        this.ResolveCore(value, depth: 0, FunctionCallerRole.RendererPropertyBinding, out _);

    public JsonElement? ResolveJsonElement(DynamicValue? value) =>
        this.ResolveJsonElementCore(value, depth: 0, FunctionCallerRole.RendererPropertyBinding, out _);

    public JsonElement? ResolveJsonElement(DynamicValue? value, FunctionCallerRole callerRole) =>
        this.ResolveJsonElementCore(value, depth: 0, callerRole, out _);

    internal string? ResolveCheck(DynamicValue? value) =>
        this.ResolveCore(value, depth: 0, FunctionCallerRole.RendererCheck, out _);

    public void InvokeActionFunction(FunctionCallValue functionCall) =>
        this.ResolveFunction(functionCall, depth: 0, FunctionCallerRole.RendererAction, out _);

    public void ReportRendererError(RendererError error)
    {
        if (string.IsNullOrEmpty(error.SurfaceId) && string.IsNullOrEmpty(error.FunctionCallId))
        {
            error = error with { SurfaceId = session.Surface.SurfaceId };
        }

        session.OnRendererError?.Invoke(error);
    }

    private JsonElement? ResolveJsonElementCore(
        DynamicValue? value,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        if (value is null)
        {
            return null;
        }

        if (depth > MaxResolveDepth)
        {
            if (session.Logger is not null)
            {
                RendererLog.ResolveExceededMaxDepth(session.Logger, MaxResolveDepth);
            }

            return null;
        }

        return value switch
        {
            DynamicValue.FunctionValue { Call: var fc } => this.ResolveFunctionToElement(
                fc,
                depth,
                callerRole,
                out hasPendingOrFailedRpc
            ),
            DynamicValue.ArrayValue { Value: var arrayEl } => this.ResolveArrayLiteralToElement(
                arrayEl,
                depth,
                callerRole,
                out hasPendingOrFailedRpc
            ),
            DynamicValue.ObjectValue { Value: var dict } => this.ResolveObjectLiteralToElement(
                dict,
                depth,
                callerRole,
                out hasPendingOrFailedRpc
            ),
            _ => session.Surface.DataModel.ResolveJsonElement(value, basePath),
        };
    }

    private JsonElement ResolveFunctionToElement(
        FunctionCallValue fc,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        if (fc.Call == "@index")
        {
            hasPendingOrFailedRpc = false;
            int idx = this.EvaluateCollectionIndex(fc, depth);
            return JsonSerializer.SerializeToElement(idx, A2UiJsonSerializerContext.Default.Int32);
        }

        string? result = this.ResolveFunction(fc, depth, callerRole, out hasPendingOrFailedRpc);
        if (result is null)
        {
            return DataModel.NullElement;
        }

        string? returnType = fc.ReturnType;
        if (returnType is null && session.RpcHandler is not null && session.RpcHandler.TryGetFunction(fc.Call, out var def))
        {
            returnType = def.ReturnType;
        }

        returnType ??= GetBuiltInReturnType(fc.Call);

        if (returnType == "number" && double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
        {
            return DataModel.SerializeNumberToElement(num);
        }

        if (returnType == "boolean" && result is "true" or "false")
        {
            return JsonSerializer.SerializeToElement(result == "true", A2UiJsonSerializerContext.Default.Boolean);
        }

        if (returnType is "array" or "object" or "any" && (result.StartsWith('[') || result.StartsWith('{')))
        {
            try
            {
                using var doc = JsonDocument.Parse(result);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall back to string representation
            }
        }

        return JsonSerializer.SerializeToElement(result, A2UiJsonSerializerContext.Default.String);
    }

    private static string GetBuiltInReturnType(string call) =>
        call switch
        {
            "add" or "subtract" or "multiply" or "divide" => "number",
            "equals"
            or "not_equals"
            or "greater_than"
            or "less_than"
            or "and"
            or "or"
            or "not"
            or "contains"
            or "starts_with"
            or "ends_with"
            or "required"
            or "email"
            or "regex"
            or "length"
            or "numeric" => "boolean",
            _ => "string",
        };

    private JsonElement ResolveArrayLiteralToElement(
        JsonElement arrayEl,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (JsonElement item in arrayEl.EnumerateArray())
            {
                try
                {
                    var itemDv = JsonSerializer.Deserialize<DynamicValue>(item, this.VersionOptions);
                    JsonElement? resolved = this.ResolveJsonElementCore(
                        itemDv,
                        depth + 1,
                        callerRole,
                        out bool itemPendingOrFailed
                    );
                    hasPendingOrFailedRpc |= itemPendingOrFailed;
                    if (resolved is { } je)
                    {
                        je.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }
                }
                catch (JsonException)
                {
                    item.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.GetBuffer().AsMemory(0, (int)stream.Length));
        return doc.RootElement.Clone();
    }

    private JsonElement ResolveObjectLiteralToElement(
        IReadOnlyDictionary<string, object?> dict,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            this.WriteResolvedObjectValue(writer, dict, depth + 1, callerRole, out hasPendingOrFailedRpc);
        }

        using var doc = JsonDocument.Parse(stream.GetBuffer().AsMemory(0, (int)stream.Length));
        return doc.RootElement.Clone();
    }

    private void WriteResolvedObjectValue(
        Utf8JsonWriter writer,
        object? val,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        switch (val)
        {
            case null:
                writer.WriteNullValue();
                break;
            case DynamicValue dv:
                JsonElement? resolved = this.ResolveJsonElementCore(
                    dv,
                    depth,
                    callerRole,
                    out hasPendingOrFailedRpc
                );
                if (resolved is { } je)
                {
                    je.WriteTo(writer);
                }
                else
                {
                    writer.WriteNullValue();
                }

                break;
            case IReadOnlyDictionary<string, object?> nestedDict:
                writer.WriteStartObject();
                foreach (var (k, v) in nestedDict)
                {
                    writer.WritePropertyName(k);
                    this.WriteResolvedObjectValue(writer, v, depth + 1, callerRole, out bool childPendingOrFailed);
                    hasPendingOrFailedRpc |= childPendingOrFailed;
                }

                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable seq when val is not string:
                writer.WriteStartArray();
                foreach (var item in seq)
                {
                    this.WriteResolvedObjectValue(writer, item, depth + 1, callerRole, out bool itemPendingOrFailed);
                    hasPendingOrFailedRpc |= itemPendingOrFailed;
                }

                writer.WriteEndArray();
                break;
            default:
                DataModel.WritePrimitiveOrElementValue(writer, val);
                break;
        }
    }

    private int EvaluateCollectionIndex(FunctionCallValue fc, int depth)
    {
        if (collectionIndex is null)
        {
            throw new A2UiEvaluationException("@index cannot be called outside a collection template scope.");
        }

        int offset = 0;
        if (fc.Args is not null && fc.Args.TryGetValue("offset", out var offsetEl))
        {
            if (offsetEl.ValueKind == JsonValueKind.Number && offsetEl.TryGetInt32(out int offVal))
            {
                offset = offVal;
            }
            else
            {
                try
                {
                    var offDv = JsonSerializer.Deserialize<DynamicValue>(offsetEl, this.VersionOptions);
                    string? offResolved = this.ResolveCore(
                        offDv,
                        depth + 1,
                        FunctionCallerRole.RendererPropertyBinding,
                        out _
                    );
                    if (
                        int.TryParse(
                            offResolved,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsedOff
                        )
                    )
                    {
                        offset = parsedOff;
                    }
                }
                catch (Exception ex) when (ex is not A2UiEvaluationException)
                {
                    // default offset 0
                }
            }
        }

        return collectionIndex.Value + offset;
    }

    private string? ResolveCore(
        DynamicValue? value,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        if (value is null)
        {
            return null;
        }

        if (depth > MaxResolveDepth)
        {
            if (session.Logger is not null)
            {
                RendererLog.ResolveExceededMaxDepth(session.Logger, MaxResolveDepth);
            }

            return null;
        }

        if (value is DynamicValue.FunctionValue { Call: var fc })
        {
            return this.ResolveFunction(fc, depth, callerRole, out hasPendingOrFailedRpc);
        }

        // ArrayLiteral: resolve each element individually and return as JSON string array.
        // This handles cases like and/or where "values" is an array of DynamicValues
        // (paths, function calls, etc.) that each need recursive resolution.
        if (value is DynamicValue.ArrayValue { Value: var arrayEl })
        {
            FunctionCallerRole elementRole =
                callerRole == FunctionCallerRole.RendererAction
                    ? FunctionCallerRole.RendererAction
                    : FunctionCallerRole.RendererPropertyBinding;
            return this.ResolveArrayLiteral(arrayEl, depth, elementRole, out hasPendingOrFailedRpc);
        }

        return session.Surface.DataModel.Resolve(value, basePath);
    }

    /// <summary>
    /// Resolve a JSON array of DynamicValues by resolving each element individually.
    /// Returns a JSON string array, e.g. <c>["true","false","true"]</c>, suitable for
    /// functions like <c>and</c>/<c>or</c>.
    /// </summary>
    private string? ResolveArrayLiteral(
        JsonElement arrayEl,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        if (arrayEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var results = new List<string>();
        foreach (JsonElement item in arrayEl.EnumerateArray())
        {
            try
            {
                var itemDv = JsonSerializer.Deserialize<DynamicValue>(item, this.VersionOptions);
                string? resolved = this.ResolveCore(itemDv, depth + 1, callerRole, out bool itemPendingOrFailed);
                hasPendingOrFailedRpc |= itemPendingOrFailed;
                results.Add(resolved ?? "");
            }
            catch (JsonException ex)
            {
                if (session.Logger is not null)
                {
                    RendererLog.FailedToDeserializeArrayElement(session.Logger, ex);
                }

                results.Add("");
            }
        }

        return JsonSerializer.Serialize(results, A2UiJsonSerializerContext.Default.ListString);
    }

    private string? ResolveFunction(
        FunctionCallValue fc,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;

        // System function: @index
        if (fc.Call == "@index")
        {
            return this.EvaluateCollectionIndex(fc, depth).ToString(CultureInfo.InvariantCulture);
        }

        if (fc.Call.StartsWith('@'))
        {
            throw new A2UiEvaluationException($"Unknown system function '{fc.Call}'.")
            {
                ErrorCode = "EXPRESSION_ERROR",
            };
        }

        // Special case: formatString needs template parsing with resolver access
        if (fc.Call == "formatString")
        {
            return this.ResolveFormatString(fc, depth);
        }

        FunctionCallerRole argRole =
            callerRole == FunctionCallerRole.RendererAction
                ? FunctionCallerRole.RendererAction
                : FunctionCallerRole.RendererPropertyBinding;

        FunctionDefinition? def = null;
        if (session.RpcHandler is not null && session.RpcHandler.TryGetFunction(fc.Call, out def))
        {
            if (def.AllowedCallers == AllowedCallers.AgentOnly)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Call}' cannot be called by renderer (allowedCallers is agentOnly)."
                )
                {
                    ErrorCode = "EXPRESSION_ERROR",
                };
            }

            if (def.RequiresUserActivation && callerRole != FunctionCallerRole.RendererAction)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Call}' requires user activation and cannot be called in a non-action context."
                )
                {
                    ErrorCode = "EXPRESSION_ERROR",
                };
            }

            if (def.Implementation is null)
            {
                return this.RouteToRpcHandler(fc, depth, callerRole, out hasPendingOrFailedRpc);
            }
        }

        if (session.FunctionRegistry?.IsRegistered(fc.Call) == true)
        {
            if (
                !this.TryEvaluateStringFunctionArgs(
                    fc,
                    depth,
                    argRole,
                    out Dictionary<string, string?> resolvedArgs,
                    out hasPendingOrFailedRpc
                )
            )
            {
                return null;
            }

            return session.FunctionRegistry.Evaluate(fc.Call, resolvedArgs, this.CreateEvaluationContext(callerRole));
        }

        if (def?.Implementation is not null)
        {
            if (
                !this.TryEvaluateStringFunctionArgs(
                    fc,
                    depth,
                    argRole,
                    out Dictionary<string, string?> resolvedArgs,
                    out hasPendingOrFailedRpc
                )
            )
            {
                return null;
            }

            return def.Implementation(resolvedArgs);
        }

        if (session.FunctionRegistry is not null and not FunctionRegistry)
        {
            if (
                !this.TryEvaluateStringFunctionArgs(
                    fc,
                    depth,
                    argRole,
                    out Dictionary<string, string?> resolvedArgs,
                    out hasPendingOrFailedRpc
                )
            )
            {
                return null;
            }

            FunctionEvaluationContext evalContext = this.CreateEvaluationContext(callerRole);

            try
            {
                string? localResult = session.FunctionRegistry.Evaluate(fc.Call, resolvedArgs, evalContext);
                if (localResult is not null || session.RpcHandler is null)
                {
                    if (localResult is null && callerRole == FunctionCallerRole.RendererAction)
                    {
                        throw new A2UiEvaluationException($"Unknown action function '{fc.Call}'.")
                        {
                            ErrorCode = "EXECUTION_ERROR",
                        };
                    }

                    return localResult;
                }
            }
            catch (A2UiEvaluationException) when (session.RpcHandler is not null)
            {
                // Fall back to RPC handler if local evaluation fails for custom IFunctionRegistry
            }
        }

        if (session.RpcHandler is not null)
        {
            return this.RouteToRpcHandler(fc, depth, callerRole, out hasPendingOrFailedRpc);
        }

        if (callerRole == FunctionCallerRole.RendererAction)
        {
            throw new A2UiEvaluationException($"Unknown action function '{fc.Call}'.")
            {
                ErrorCode = "EXECUTION_ERROR",
            };
        }

        if (session.Logger is not null)
        {
            RendererLog.NoFunctionRegistryForCall(session.Logger, fc.Call);
        }

        return null;
    }

    private bool TryEvaluateStringFunctionArgs(
        FunctionCallValue fc,
        int depth,
        FunctionCallerRole argRole,
        out Dictionary<string, string?> resolvedArgs,
        out bool hasPendingOrFailedRpc
    )
    {
        resolvedArgs = this.EvaluateFunctionArgs(
            fc.Call,
            fc.Args,
            argRole,
            (dv, out pending) => this.ResolveCore(dv, depth + 1, argRole, out pending),
            fallbackValue: null,
            out hasPendingOrFailedRpc
        );
        return !hasPendingOrFailedRpc;
    }

    private FunctionEvaluationContext CreateEvaluationContext(FunctionCallerRole callerRole) =>
        new()
        {
            CallerRole = callerRole,
            UrlLauncher = session.Options?.UrlLauncher,
            CollectionIndex = collectionIndex,
            ProtocolVersion = session.Surface.ProtocolVersion,
        };

    private delegate T ArgValueResolver<out T>(DynamicValue? value, out bool hasPendingOrFailedRpc);

    private Dictionary<string, T> EvaluateFunctionArgs<T>(
        string functionName,
        Dictionary<string, JsonElement>? rawArgs,
        FunctionCallerRole argRole,
        ArgValueResolver<T> resolveArg,
        T fallbackValue,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        var resolvedArgs = new Dictionary<string, T>();
        if (rawArgs is null || rawArgs.Count == 0)
        {
            return resolvedArgs;
        }

        foreach (var (key, jsonEl) in rawArgs)
        {
            try
            {
                var argValue = JsonSerializer.Deserialize<DynamicValue>(jsonEl, this.VersionOptions);
                resolvedArgs[key] = resolveArg(argValue, out bool argPendingOrFailed);
                hasPendingOrFailedRpc |= argPendingOrFailed;
            }
            catch (JsonException ex)
            {
                if (session.Logger is not null)
                {
                    RendererLog.FailedToDeserializeFunctionArg(session.Logger, key, functionName, ex);
                }

                if (argRole == FunctionCallerRole.RendererAction)
                {
                    throw;
                }

                resolvedArgs[key] = fallbackValue;
            }
        }

        return resolvedArgs;
    }

    private string? RouteToRpcHandler(
        FunctionCallValue fc,
        int depth,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;

        if (session.Surface.ProtocolVersion != A2UiProtocol.Version10)
        {
            if (callerRole == FunctionCallerRole.RendererAction)
            {
                throw new A2UiEvaluationException($"Unknown function '{fc.Call}'.");
            }

            if (session.Logger is not null)
            {
                RendererLog.NoFunctionRegistryForCall(session.Logger, fc.Call);
            }

            return null;
        }

        string? resolvedCatalogId = !string.IsNullOrEmpty(fc.CatalogId) ? fc.CatalogId : session.Surface.CatalogId;
        if (string.IsNullOrEmpty(resolvedCatalogId))
        {
            throw new A2UiEvaluationException($"Cannot route agent function '{fc.Call}' without a catalogId.")
            {
                ErrorCode = "CATALOG_ERROR",
            };
        }

        if (
            !string.IsNullOrEmpty(fc.CatalogId)
            && !string.Equals(fc.CatalogId, session.Surface.CatalogId, StringComparison.Ordinal)
            && !session.Catalog.IsKnownCatalog(fc.CatalogId)
            && !IsRpcFunctionRegisteredForCatalog(session.RpcHandler, fc.Call, fc.CatalogId)
        )
        {
            throw new A2UiEvaluationException(
                $"Catalog '{fc.CatalogId}' is not registered for function '{fc.Call}'."
            )
            {
                ErrorCode = "CATALOG_ERROR",
            };
        }

        FunctionCallerRole argRole =
            callerRole == FunctionCallerRole.RendererAction
                ? FunctionCallerRole.RendererAction
                : FunctionCallerRole.RendererPropertyBinding;

        Dictionary<string, JsonElement> resolvedArgsElements = this.EvaluateFunctionArgs(
            fc.Call,
            fc.Args,
            argRole,
            (dv, out pending) =>
                this.ResolveJsonElementCore(dv, depth + 1, argRole, out pending) ?? DataModel.NullElement,
            fallbackValue: DataModel.NullElement,
            out bool argsPendingOrFailed
        );

        if (argsPendingOrFailed)
        {
            hasPendingOrFailedRpc = true;
            return null;
        }

        var resolvedFc = new FunctionCallValue
        {
            Call = fc.Call,
            CatalogId = resolvedCatalogId,
            Args = resolvedArgsElements,
            ReturnType = fc.ReturnType,
        };

        return this.InvokeRpcAgentFunction(fc.Call, resolvedFc, callerRole, out hasPendingOrFailedRpc);
    }

    private static bool IsRpcFunctionRegisteredForCatalog(
        RpcHandler? handler,
        string functionName,
        string catalogId
    )
    {
        if (handler is null)
        {
            return false;
        }

        if (handler.TryGetFunction($"{catalogId}:{functionName}", out _))
        {
            return true;
        }

        return handler.TryGetFunction(functionName, out var def)
            && (def.CatalogId is null || string.Equals(def.CatalogId, catalogId, StringComparison.Ordinal));
    }

    private string? InvokeRpcAgentFunction(
        string functionName,
        FunctionCallValue resolvedFc,
        FunctionCallerRole callerRole,
        out bool hasPendingOrFailedRpc
    )
    {
        hasPendingOrFailedRpc = false;
        if (session.RpcHandler is null)
        {
            return null;
        }

        string? cacheKey = callerRole == FunctionCallerRole.RendererAction ? null : BuildRpcCacheKey(resolvedFc);
        if (cacheKey is not null && session.RpcCache is not null)
        {
            if (session.RpcCache.TryGetSettled(cacheKey, out bool hasError, out string? cachedValue))
            {
                if (hasError)
                {
                    hasPendingOrFailedRpc = true;
                    return null;
                }

                return cachedValue;
            }

            if (!session.RpcCache.TryMarkInFlight(cacheKey))
            {
                hasPendingOrFailedRpc = true;
                return null;
            }
        }

        Task<FunctionResponse> rpcTask;
        try
        {
            rpcTask = session.RpcHandler.InvokeAgentFunctionAsync(
                session.Surface.SurfaceId,
                resolvedFc,
                session.SurfaceCancellation
            );
        }
        catch (OperationCanceledException)
        {
            if (cacheKey is not null)
            {
                session.RpcCache?.RemoveInFlight(cacheKey);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (cacheKey is not null)
            {
                session.RpcCache?.RemoveInFlight(cacheKey);
            }

            throw new A2UiEvaluationException($"RPC invocation of '{functionName}' failed: {ex.Message}", ex);
        }

        if (rpcTask.IsCompleted)
        {
            try
            {
                FunctionResponse response = rpcTask.GetAwaiter().GetResult();
                if (response.Error is not null)
                {
                    if (cacheKey is not null)
                    {
                        session.RpcCache?.Settle(cacheKey, hasError: true, value: null);
                    }

                    if (callerRole != FunctionCallerRole.RendererAction)
                    {
                        this.ReportRendererError(
                            new RendererError
                            {
                                Code = "EXPRESSION_ERROR",
                                FunctionCallId = response.FunctionCallId,
                                Message = response.Error.Message,
                            }
                        );
                    }

                    throw new A2UiEvaluationException(
                        $"RPC agent function '{functionName}' failed: {response.Error.Message} ({response.Error.Code})",
                        response.FunctionCallId
                    )
                    {
                        ErrorCode = "EXPRESSION_ERROR",
                    };
                }

                string? valStr = ExtractResponseString(response);
                if (cacheKey is not null)
                {
                    session.RpcCache?.Settle(cacheKey, hasError: false, value: valStr);
                }

                return valStr;
            }
            catch (OperationCanceledException)
            {
                if (cacheKey is not null)
                {
                    session.RpcCache?.RemoveInFlight(cacheKey);
                }

                throw;
            }
            catch (A2UiEvaluationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (cacheKey is not null)
                {
                    session.RpcCache?.RemoveInFlight(cacheKey);
                }

                throw new A2UiEvaluationException($"RPC invocation of '{functionName}' failed: {ex.Message}", ex);
            }
        }

        hasPendingOrFailedRpc = true;
        _ = this.ObserveRpcTaskAsync(rpcTask, cacheKey, callerRole);
        return null;
    }

    private async Task ObserveRpcTaskAsync(
        Task<FunctionResponse> rpcTask,
        string? cacheKey,
        FunctionCallerRole callerRole
    )
    {
        try
        {
            FunctionResponse response = await rpcTask.ConfigureAwait(false);
            if (response.Error is not null)
            {
                if (cacheKey is not null)
                {
                    session.RpcCache?.Settle(cacheKey, hasError: true, value: null);
                }

                if (!session.SurfaceCancellation.IsCancellationRequested)
                {
                    var rendererError = new RendererError
                    {
                        Code = "EXPRESSION_ERROR",
                        FunctionCallId = response.FunctionCallId,
                        Message = response.Error.Message,
                    };

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!session.SurfaceCancellation.IsCancellationRequested)
                        {
                            this.ReportRendererError(rendererError);
                            if (callerRole != FunctionCallerRole.RendererAction)
                            {
                                session.OnRpcSettled?.Invoke(session.Surface);
                            }
                        }
                    });
                }

                return;
            }

            if (cacheKey is not null)
            {
                string? valStr = ExtractResponseString(response);
                session.RpcCache?.Settle(cacheKey, hasError: false, value: valStr);
            }

            if (
                !session.SurfaceCancellation.IsCancellationRequested
                && callerRole != FunctionCallerRole.RendererAction
                && session.OnRpcSettled is not null
            )
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!session.SurfaceCancellation.IsCancellationRequested)
                    {
                        session.OnRpcSettled(session.Surface);
                    }
                });
            }
        }
        catch
        {
            if (cacheKey is not null)
            {
                session.RpcCache?.RemoveInFlight(cacheKey);
            }
        }
    }

    private static string? ExtractResponseString(FunctionResponse response)
    {
        if (response.Value is { } val)
        {
            return val.ValueKind switch
            {
                JsonValueKind.String => val.GetString(),
                JsonValueKind.Null => null,
                _ => val.GetRawText(),
            };
        }

        return null;
    }

    private static string BuildRpcCacheKey(FunctionCallValue fc)
    {
        var sb = new StringBuilder();
        sb.Append(fc.CatalogId ?? string.Empty).Append(':').Append(fc.Call);
        if (fc.Args is { Count: > 0 })
        {
            foreach (var kv in fc.Args.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value.GetRawText());
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Handle <c>formatString</c> by parsing the template with <see cref="ExpressionParser"/>
    /// and resolving all embedded <c>${...}</c> tokens (paths, function calls, literals).
    /// </summary>
    private string? ResolveFormatString(FunctionCallValue fc, int depth)
    {
        // Get the raw "value" arg — it's a string literal containing ${} tokens
        string? template = null;
        if (fc.Args is not null && fc.Args.TryGetValue("value", out JsonElement valEl))
        {
            try
            {
                var valDv = JsonSerializer.Deserialize<DynamicValue>(valEl, this.VersionOptions);
                // The template is always a string literal like "${formatDate(value: ${/start}, format: 'E, MMM d')}"
                template = valDv is DynamicValue.StringValue sv ? sv.Value : null;
            }
            catch (JsonException ex)
            {
                if (session.Logger is not null)
                {
                    RendererLog.FailedToDeserializeFormatStringArg(session.Logger, ex);
                }
            }
        }

        if (string.IsNullOrEmpty(template))
        {
            return "";
        }

        try
        {
            IReadOnlyList<ExpressionToken> tokens = s_expressionParser.Parse(template);

            var sb = new StringBuilder();
            foreach (ExpressionToken token in tokens)
            {
                string? resolved = this.ResolveExpressionToken(token, depth + 1);
                if (resolved is not null)
                {
                    sb.Append(resolved);
                }
            }
            return sb.ToString();
        }
        catch (A2UiExpressionException ex)
        {
            if (session.Logger is not null)
            {
                RendererLog.FailedToParseFormatStringTemplate(session.Logger, ex);
            }

            return template;
        }
    }

    /// <summary>
    /// Resolve a single <see cref="ExpressionToken"/> to its string value.
    /// </summary>
    private string? ResolveExpressionToken(ExpressionToken token, int depth)
    {
        if (depth > MaxResolveDepth)
        {
            if (session.Logger is not null)
            {
                RendererLog.ExpressionTokenExceededMaxDepth(session.Logger, MaxResolveDepth);
            }

            return null;
        }

        return token switch
        {
            LiteralToken lit => lit.Value,
            PathToken path => this.ResolveExpressionPath(path.Path, depth),
            BoolToken b => b.Value ? "true" : "false",
            NumberToken n => n.Value.ToString(CultureInfo.InvariantCulture),
            FunctionCallToken fc => this.ResolveExpressionFunctionCall(fc, depth),
            _ => null,
        };
    }

    /// <summary>
    /// Resolve a path token from an expression. Scope handling is delegated to
    /// <see cref="ResolveCore"/>, which threads the current <c>basePath</c> into
    /// <see cref="DataModel.Resolve(DynamicValue?, string?)"/>.
    /// </summary>
    private string? ResolveExpressionPath(string path, int depth) =>
        this.ResolveCore(
            DynamicValue.FromPath(path),
            depth,
            FunctionCallerRole.RendererPropertyBinding,
            out _
        );

    /// <summary>
    /// Resolve a <see cref="FunctionCallToken"/> by resolving each arg token,
    /// then calling the local function registry or local implementation without routing to RPC.
    /// </summary>
    private string? ResolveExpressionFunctionCall(FunctionCallToken fc, int depth)
    {
        FunctionDefinition? def = null;
        if (session.RpcHandler is not null && session.RpcHandler.TryGetFunction(fc.Name, out def))
        {
            if (def.AllowedCallers == AllowedCallers.AgentOnly)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Name}' cannot be called by renderer (allowedCallers is agentOnly)."
                )
                {
                    ErrorCode = "EXPRESSION_ERROR",
                };
            }

            if (def.RequiresUserActivation)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Name}' requires user activation and cannot be called in a non-action context."
                )
                {
                    ErrorCode = "EXPRESSION_ERROR",
                };
            }
        }

        var resolvedArgs = new Dictionary<string, string?>();
        foreach (var (key, argToken) in fc.Args)
        {
            resolvedArgs[key] = this.ResolveExpressionToken(argToken, depth + 1);
        }

        if (session.FunctionRegistry is not null)
        {
            FunctionEvaluationContext evalContext = this.CreateEvaluationContext(
                FunctionCallerRole.RendererPropertyBinding
            );

            if (session.FunctionRegistry.IsRegistered(fc.Name))
            {
                return session.FunctionRegistry.Evaluate(fc.Name, resolvedArgs, evalContext);
            }

            if (session.FunctionRegistry is not FunctionRegistry)
            {
                try
                {
                    string? localResult = session.FunctionRegistry.Evaluate(fc.Name, resolvedArgs, evalContext);
                    if (localResult is not null)
                    {
                        return localResult;
                    }
                }
                catch (A2UiEvaluationException)
                {
                    // Fall through to report unimplemented template expression
                }
            }
        }

        if (def?.Implementation is not null)
        {
            return def.Implementation(resolvedArgs);
        }

        if (session.Logger is not null)
        {
            RendererLog.NoFunctionRegistryForExpression(session.Logger, fc.Name);
        }

        this.ReportRendererError(
            new RendererError
            {
                Code = "EXPRESSION_ERROR",
                SurfaceId = session.Surface.SurfaceId,
                Message = $"Unknown function '{fc.Name}' in expression template.",
            }
        );

        return null;
    }

    public double? GetComponentWeight(string componentId) =>
        session.Surface.Components.TryGetValue(componentId, out var comp) ? comp.Weight : null;

    internal bool AreComponentChecksPassing(string componentId)
    {
        if (session.Surface.Components.TryGetValue(componentId, out var comp))
        {
            return CheckHelper.AllChecksPassing(comp, this);
        }

        return true;
    }

    internal IReadOnlyList<(Control Control, double Weight)> RenderChildrenWithWeights(string parentId)
    {
        if (session.Surface.Components.TryGetValue(parentId, out var comp) && comp.Children is not null)
        {
            if (comp.Children.Ids is { } ids)
            {
                var list = new List<(Control Control, double Weight)>(ids.Length);
                foreach (var id in ids)
                {
                    var ctrl = this.RenderChild(id);
                    if (ctrl is not null)
                    {
                        double weight = this.GetComponentWeight(id) ?? 0;
                        list.Add((ctrl, weight));
                    }
                }

                return list;
            }

            if (comp.Children.Template is { } tmpl)
            {
                return this.ExpandTemplate(tmpl).ConvertAll(c => (c, 0.0));
            }
        }

        var legacyList = new List<(Control Control, double Weight)>();
        foreach (var c in session.Surface.Components.Values.Where(c => c.Parent == parentId))
        {
            var ctrl = this.RenderChild(c.Id);
            if (ctrl is not null)
            {
                double weight = c.Weight ?? 0;
                legacyList.Add((ctrl, weight));
            }
        }

        return legacyList;
    }

    public void UpdateDataModel(string path, string? value)
    {
        JsonElement? element = value is not null
            ? JsonSerializer.SerializeToElement(value, A2UiJsonSerializerContext.Default.String)
            : null;
        this.UpdateDataModel(path, element);
    }

    public void UpdateDataModel(string path, JsonElement? value)
    {
        try
        {
            var update = new UpdateDataModel
            {
                SurfaceId = session.Surface.SurfaceId,
                Path = path,
                Value = value,
            };
            session.Surface.DataModel.Apply(update);
            session.OnDataModelChanged(session.Surface.SurfaceId);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            if (session.Logger is not null)
            {
                RendererLog.FailedToUpdateDataModel(session.Logger, path, ex);
            }
        }
    }
}

/// <summary>Event arguments for a user action triggered on a rendered A2UI component.</summary>
/// <param name="SurfaceId">Identifier of the surface containing the source component.</param>
/// <param name="EventName">Action event name from the component definition.</param>
/// <param name="Payload">Resolved action context data, or <see langword="null"/>.</param>
/// <param name="ComponentId">Optional component ID that triggered the action.</param>
/// <param name="UserMessage">Optional human-readable description of the action (v1.0).</param>
public sealed record UserActionEventArgs(
    string SurfaceId,
    string EventName,
    object? Payload,
    string? ComponentId = null,
    string? UserMessage = null
);

/// <summary>Event arguments when the data model is mutated via two-way binding.</summary>
/// <param name="SurfaceId">Identifier of the surface whose data model changed.</param>
public sealed record DataModelChangedEventArgs(string SurfaceId);

internal static partial class RendererLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Cannot detach control from unknown parent type {ParentTypeName}"
    )]
    public static partial void CannotDetachFromUnknownParent(ILogger logger, string parentTypeName);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Resolve exceeded max depth ({MaxDepth}), returning null"
    )]
    public static partial void ResolveExceededMaxDepth(ILogger logger, int maxDepth);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize array element for function arg"
    )]
    public static partial void FailedToDeserializeArrayElement(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "FunctionCall '{FunctionName}' encountered but no function registry configured"
    )]
    public static partial void NoFunctionRegistryForCall(ILogger logger, string functionName);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize arg '{ArgName}' for function '{FunctionName}'"
    )]
    public static partial void FailedToDeserializeFunctionArg(
        ILogger logger,
        string argName,
        string functionName,
        Exception exception
    );

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Failed to deserialize formatString 'value' arg")]
    public static partial void FailedToDeserializeFormatStringArg(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Failed to parse formatString template")]
    public static partial void FailedToParseFormatStringTemplate(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "ResolveExpressionToken exceeded max depth ({MaxDepth})"
    )]
    public static partial void ExpressionTokenExceededMaxDepth(ILogger logger, int maxDepth);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Expression function '{FunctionName}' encountered but no function registry configured"
    )]
    public static partial void NoFunctionRegistryForExpression(ILogger logger, string functionName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Failed to update data model at path '{Path}'")]
    public static partial void FailedToUpdateDataModel(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Debug,
        Message = "Render component {ComponentId} (type={ComponentType}, parent={ParentComponentId})"
    )]
    public static partial void RenderComponent(
        ILogger logger,
        string componentId,
        string componentType,
        string parentComponentId
    );

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Unknown component type rendered as fallback: id={ComponentId}, type={ComponentType}"
    )]
    public static partial void UnknownComponentTypeRendered(ILogger logger, string componentId, string componentType);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Debug,
        Message = "Template instantiated: {ComponentId} (template={TemplateType}) × {InstanceCount}"
    )]
    public static partial void TemplateInstantiated(
        ILogger logger,
        string componentId,
        string templateType,
        int instanceCount
    );

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Warning,
        Message = "Cyclic reference detected for component '{ComponentId}' in surface '{SurfaceId}'"
    )]
    public static partial void CyclicReferenceDetected(
        ILogger logger,
        string componentId,
        string surfaceId
    );

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Warning,
        Message = "Failed to evaluate button action for component '{ComponentId}'"
    )]
    public static partial void ActionInvocationFailed(
        ILogger logger,
        string componentId,
        Exception exception
    );
}
