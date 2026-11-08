using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Avalonia.Functions;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
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
    RpcHandler? rpcHandler = null
)
{
    private readonly CatalogRegistry _catalog = catalog;
    private readonly IFunctionRegistry? _functionRegistry = functionRegistry;
    private readonly ILogger<A2UiRenderer> _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<A2UiRenderer>();
    private readonly Dictionary<string, Dictionary<string, Control>> _surfaceCaches = [];
    private readonly Dictionary<string, CancellationTokenSource> _surfaceCts = [];

    /// <summary>Optional RPC handler for agent-backed catalog functions.</summary>
    public RpcHandler? RpcHandler { get; } = rpcHandler;

    /// <summary>
    /// Render or update the complete surface.
    /// Returns the root control for insertion into the visual tree.
    /// </summary>
    public Control Render(Surface surface)
    {
        Dispatcher.UIThread.VerifyAccess();

        using IDisposable versionScope = DynamicValueConverter.EnterVersionScope(
            surface.ProtocolVersion ?? A2UiProtocol.Version091
        );
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

        if (!this._surfaceCaches.TryGetValue(surface.SurfaceId, out var cache))
        {
            cache = [];
            this._surfaceCaches[surface.SurfaceId] = cache;
        }

        if (!this._surfaceCts.TryGetValue(surface.SurfaceId, out var cts))
        {
            cts = new CancellationTokenSource();
            this._surfaceCts[surface.SurfaceId] = cts;
        }

        var context = new RenderContext(
            surface,
            this._catalog,
            cache,
            this._functionRegistry,
            (surfaceId, eventName, payload, componentId, userMessage) =>
                UserActionFired?.Invoke(this, new(surfaceId, eventName, payload, componentId, userMessage)),
            (surfaceId) => DataModelChanged?.Invoke(this, new(surfaceId)),
            logger: this._logger,
            surfaceCancellation: cts.Token,
            rpcHandler: this.RpcHandler
        );

        var roots = surface.GetRootComponents().ToList();

        if (roots.Count == 1)
        {
            return this.RenderComponent(roots[0], surface, context);
        }

        var container = new StackPanel { Spacing = 8 };
        foreach (var root in roots)
        {
            container.Children.Add(this.RenderComponent(root, surface, context));
        }

        return container;
    }

    /// <summary>Raised when a user interacts with a rendered component (e.g., button click).</summary>
    public event EventHandler<UserActionEventArgs>? UserActionFired;

    /// <summary>Raised when the data model is mutated via two-way binding (e.g., text input).</summary>
    public event EventHandler<DataModelChangedEventArgs>? DataModelChanged;

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
            if (
                cache is not null
                && cache.TryGetValue(component.Id, out var existing)
                && entry.Update(existing, component, surface.DataModel, context)
            )
            {
                ApplyAccessibility(existing, component, context);
                return existing;
            }

            var control = entry.Create(component, surface.DataModel, context);
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
        this._surfaceCaches.Remove(surfaceId);

        if (this._surfaceCts.Remove(surfaceId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    internal static void ApplyAccessibility(Control control, A2UiComponent component, IRenderContext context)
    {
        var acc = component.Accessibility;

        string? label = acc?.Label is not null ? context.Resolve(acc.Label) : null;
        AutomationProperties.SetName(control, string.IsNullOrEmpty(label) ? null : label);

        string? description = acc?.Description is not null ? context.Resolve(acc.Description) : null;
        AutomationProperties.SetHelpText(control, string.IsNullOrEmpty(description) ? null : description);

        var setting = acc?.Live?.ToLowerInvariant() switch
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

        control.IsVisible = !isHidden;
        AutomationProperties.SetAccessibilityView(
            control,
            isHidden ? AccessibilityView.Raw : AccessibilityView.Content
        );
    }
}

internal sealed class RenderContext(
    Surface surface,
    CatalogRegistry catalog,
    Dictionary<string, Control> cache,
    IFunctionRegistry? functionRegistry,
    Action<string, string, object?, string?, string?> fireAction,
    Action<string> onDataModelChanged,
    ILogger? logger = null,
    CancellationToken surfaceCancellation = default,
    string? basePath = null,
    int? collectionIndex = null,
    RpcHandler? rpcHandler = null,
    HashSet<string>? activeAncestors = null
) : IRenderContext
{
    internal HashSet<string> ActiveAncestors { get; } = activeAncestors ?? [];
    internal Surface Surface => surface;
    public ILogger? Logger => logger;
    public CancellationToken SurfaceCancellation => surfaceCancellation;
    public int? CollectionIndex => collectionIndex;
    public RpcHandler? RpcHandler => rpcHandler;

    public IRenderContext WithIndex(int index) =>
        new RenderContext(
            surface,
            catalog,
            cache,
            functionRegistry,
            fireAction,
            onDataModelChanged,
            logger,
            surfaceCancellation,
            basePath,
            index,
            rpcHandler,
            this.ActiveAncestors
        );

    private const int MaxResolveDepth = 32;
    private static readonly JsonSerializerOptions s_jsonOptions = A2UiJsonUtilities.DefaultOptions;
    private static readonly ExpressionParser s_expressionParser = new();

    public Control? RenderChild(string? childId)
    {
        if (childId is null || !surface.Components.TryGetValue(childId, out var c))
        {
            return null;
        }

        if (this.ActiveAncestors.Contains(c.Id))
        {
            if (logger is not null)
            {
                RendererLog.CyclicReferenceDetected(logger, c.Id, surface.SurfaceId);
            }

            return null;
        }

        if (
            surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(c.CatalogId)
            && !catalog.IsKnownCatalog(c.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Component '{c.Id}' specifies unregistered catalog '{c.CatalogId}'."
            );
        }

        if (!catalog.TryResolveEntry(surface, c, out var entry))
        {
            if (logger is not null)
            {
                RendererLog.UnknownComponentTypeRendered(logger, c.Id, c.Component);
            }

            return null;
        }

        this.ActiveAncestors.Add(c.Id);
        try
        {
            if (cache.TryGetValue(c.Id, out var existing) && entry.Update(existing, c, surface.DataModel, this))
            {
                A2UiRenderer.ApplyAccessibility(existing, c, this);
                DetachFromParent(existing, logger);
                return existing;
            }

            var control = entry.Create(c, surface.DataModel, this);
            A2UiRenderer.ApplyAccessibility(control, c, this);
            cache[c.Id] = control;
            return control;
        }
        finally
        {
            this.ActiveAncestors.Remove(c.Id);
        }
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
        if (surface.Components.TryGetValue(parentId, out var comp) && comp.Children is not null)
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
        return surface
            .Components.Values.Where(c => c.Parent == parentId)
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
        int count = surface.DataModel.GetArrayLength(tmpl.Path);
        if (count <= 0)
        {
            return [];
        }

        if (!surface.Components.TryGetValue(tmpl.ComponentId, out var templateComp))
        {
            return [];
        }

        if (
            surface.ProtocolVersion == A2UiProtocol.Version10
            && !string.IsNullOrEmpty(templateComp.CatalogId)
            && !catalog.IsKnownCatalog(templateComp.CatalogId)
        )
        {
            throw new A2UiCatalogException(
                $"Component '{templateComp.Id}' specifies unregistered catalog '{templateComp.CatalogId}'."
            );
        }

        if (!catalog.TryResolveEntry(surface, templateComp, out var entry))
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
                surface,
                catalog,
                instanceCache,
                functionRegistry,
                fireAction,
                onDataModelChanged,
                logger,
                surfaceCancellation,
                itemBasePath,
                collectionIndex: i,
                rpcHandler: rpcHandler,
                activeAncestors: this.ActiveAncestors
            );

            var control = entry.Create(templateComp, surface.DataModel, scopedContext);
            A2UiRenderer.ApplyAccessibility(control, templateComp, scopedContext);
            controls.Add(control);
        }

        if (logger is not null)
        {
            RendererLog.TemplateInstantiated(logger, tmpl.ComponentId, templateComp.Component, controls.Count);
        }

        return controls;
    }

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) =>
        fireAction(surface.SurfaceId, eventName, payload, componentId, null);

    public void FireUserAction(string eventName, object? payload, string? componentId, string? userMessage) =>
        fireAction(surface.SurfaceId, eventName, payload, componentId, userMessage);

    public string? Resolve(DynamicValue? value)
    {
        using IDisposable versionScope = DynamicValueConverter.EnterVersionScope(
            surface.ProtocolVersion ?? A2UiProtocol.Version091
        );
        return this.ResolveCore(value, depth: 0);
    }

    private string? ResolveCore(DynamicValue? value, int depth)
    {
        if (value is null)
        {
            return null;
        }

        if (depth > MaxResolveDepth)
        {
            if (logger is not null)
            {
                RendererLog.ResolveExceededMaxDepth(logger, MaxResolveDepth);
            }

            return null;
        }

        if (value is DynamicValue.FunctionValue { Call: var fc })
        {
            return this.ResolveFunction(fc, depth);
        }

        // ArrayLiteral: resolve each element individually and return as JSON string array.
        // This handles cases like and/or where "values" is an array of DynamicValues
        // (paths, function calls, etc.) that each need recursive resolution.
        if (value is DynamicValue.ArrayValue { Value: var arrayEl })
        {
            return this.ResolveArrayLiteral(arrayEl, depth);
        }

        return surface.DataModel.Resolve(value, basePath);
    }

    /// <summary>
    /// Resolve a JSON array of DynamicValues by resolving each element individually.
    /// Returns a JSON string array, e.g. <c>["true","false","true"]</c>, suitable for
    /// functions like <c>and</c>/<c>or</c> that use <see cref="BuiltInFunctions.ParseJsonStringArray"/>.
    /// </summary>
    private string? ResolveArrayLiteral(JsonElement arrayEl, int depth)
    {
        if (arrayEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var results = new List<string>();
        foreach (JsonElement item in arrayEl.EnumerateArray())
        {
            try
            {
                var itemDv = JsonSerializer.Deserialize<DynamicValue>(item.GetRawText(), s_jsonOptions);
                string? resolved = this.ResolveCore(itemDv, depth + 1);
                results.Add(resolved ?? "");
            }
            catch (JsonException ex)
            {
                if (logger is not null)
                {
                    RendererLog.FailedToDeserializeArrayElement(logger, ex);
                }

                results.Add("");
            }
        }

        return JsonSerializer.Serialize(results, A2UiJsonSerializerContext.Default.ListString);
    }

    private string? ResolveFunction(FunctionCallValue fc, int depth)
    {
        // System function: @index
        if (fc.Call == "@index")
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
                        var offDv = JsonSerializer.Deserialize<DynamicValue>(offsetEl.GetRawText(), s_jsonOptions);
                        string? offResolved = this.ResolveCore(offDv, depth + 1);
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

            return (collectionIndex.Value + offset).ToString(CultureInfo.InvariantCulture);
        }

        // Special case: formatString needs template parsing with resolver access
        if (fc.Call == "formatString")
        {
            return this.ResolveFormatString(fc, depth);
        }

        if (rpcHandler is not null && rpcHandler.TryGetFunction(fc.Call, out var def))
        {
            if (def.AllowedCallers == AllowedCallers.AgentOnly)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Call}' cannot be called by renderer (allowedCallers is agentOnly)."
                );
            }

            if (def.Implementation is null)
            {
                return this.InvokeRpcAgentFunction(fc.Call, fc);
            }
        }

        if (functionRegistry is not null)
        {
            var resolvedArgs = new Dictionary<string, string?>();
            if (fc.Args is not null)
            {
                foreach (var (key, jsonEl) in fc.Args)
                {
                    try
                    {
                        var argValue = JsonSerializer.Deserialize<DynamicValue>(jsonEl.GetRawText(), s_jsonOptions);
                        resolvedArgs[key] = this.ResolveCore(argValue, depth + 1);
                    }
                    catch (JsonException ex)
                    {
                        if (logger is not null)
                        {
                            RendererLog.FailedToDeserializeFunctionArg(logger, key, fc.Call, ex);
                        }

                        resolvedArgs[key] = null;
                    }
                }
            }

            try
            {
                string? localResult = functionRegistry.Evaluate(fc.Call, resolvedArgs);
                if (localResult is not null || rpcHandler is null)
                {
                    return localResult;
                }
            }
            catch (A2UiEvaluationException) when (rpcHandler is not null)
            {
                // Fall back to RPC handler if local evaluation fails
            }
        }

        if (rpcHandler is not null)
        {
            return this.InvokeRpcAgentFunction(fc.Call, fc);
        }

        if (logger is not null)
        {
            RendererLog.NoFunctionRegistryForCall(logger, fc.Call);
        }

        return null;
    }

    private string? InvokeRpcAgentFunction(string functionName, FunctionCallValue fc)
    {
        if (rpcHandler is null)
        {
            return null;
        }

        try
        {
            var response = rpcHandler.InvokeAgentFunctionAsync(surface.SurfaceId, fc, surfaceCancellation)
                .GetAwaiter()
                .GetResult();

            if (response.Error is not null)
            {
                throw new A2UiEvaluationException(
                    $"RPC agent function '{functionName}' failed: {response.Error.Message} ({response.Error.Code})"
                );
            }

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (A2UiEvaluationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new A2UiEvaluationException($"RPC invocation of '{functionName}' failed: {ex.Message}", ex);
        }
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
                var valDv = JsonSerializer.Deserialize<DynamicValue>(valEl.GetRawText(), s_jsonOptions);
                // The template is always a string literal like "${formatDate(value: ${/start}, format: 'E, MMM d')}"
                template = valDv is DynamicValue.StringValue sv ? sv.Value : null;
            }
            catch (JsonException ex)
            {
                if (logger is not null)
                {
                    RendererLog.FailedToDeserializeFormatStringArg(logger, ex);
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
            if (logger is not null)
            {
                RendererLog.FailedToParseFormatStringTemplate(logger, ex);
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
            if (logger is not null)
            {
                RendererLog.ExpressionTokenExceededMaxDepth(logger, MaxResolveDepth);
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
        this.ResolveCore(DynamicValue.FromPath(path), depth);

    /// <summary>
    /// Resolve a <see cref="FunctionCallToken"/> by resolving each arg token,
    /// then calling the function registry.
    /// </summary>
    private string? ResolveExpressionFunctionCall(FunctionCallToken fc, int depth)
    {
        if (rpcHandler is not null && rpcHandler.TryGetFunction(fc.Name, out var def))
        {
            if (def.AllowedCallers == AllowedCallers.AgentOnly)
            {
                throw new A2UiEvaluationException(
                    $"Function '{fc.Name}' cannot be called by renderer (allowedCallers is agentOnly)."
                );
            }

            if (def.Implementation is null)
            {
                var argsDict = new Dictionary<string, JsonElement>();
                foreach (var (k, v) in fc.Args)
                {
                    string? resolvedVal = this.ResolveExpressionToken(v, depth + 1);
                    argsDict[k] = JsonSerializer.SerializeToElement(resolvedVal, A2UiJsonSerializerContext.Default.String);
                }

                var fcValue = new FunctionCallValue
                {
                    Call = fc.Name,
                    Args = argsDict,
                };

                return this.InvokeRpcAgentFunction(fc.Name, fcValue);
            }
        }

        var resolvedArgs = new Dictionary<string, string?>();
        foreach (var (key, argToken) in fc.Args)
        {
            resolvedArgs[key] = this.ResolveExpressionToken(argToken, depth + 1);
        }

        if (functionRegistry is not null)
        {
            try
            {
                string? localResult = functionRegistry.Evaluate(fc.Name, resolvedArgs);
                if (localResult is not null || rpcHandler is null)
                {
                    return localResult;
                }
            }
            catch (A2UiEvaluationException) when (rpcHandler is not null)
            {
                // Fall back to RPC handler
            }
        }

        if (rpcHandler is not null)
        {
            var argsDict = new Dictionary<string, JsonElement>();
            foreach (var (k, v) in resolvedArgs)
            {
                argsDict[k] = JsonSerializer.SerializeToElement(v, A2UiJsonSerializerContext.Default.String);
            }

            var fcValue = new FunctionCallValue
            {
                Call = fc.Name,
                Args = argsDict,
            };

            return this.InvokeRpcAgentFunction(fc.Name, fcValue);
        }

        if (logger is not null)
        {
            RendererLog.NoFunctionRegistryForExpression(logger, fc.Name);
        }

        return null;
    }

    public double? GetComponentWeight(string componentId) =>
        surface.Components.TryGetValue(componentId, out var comp) ? comp.Weight : null;

    internal bool AreComponentChecksPassing(string componentId)
    {
        if (surface.Components.TryGetValue(componentId, out var comp))
        {
            return CheckHelper.AllChecksPassing(comp, this);
        }

        return true;
    }

    internal IReadOnlyList<(Control Control, double Weight)> RenderChildrenWithWeights(string parentId)
    {
        if (surface.Components.TryGetValue(parentId, out var comp) && comp.Children is not null)
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
        foreach (var c in surface.Components.Values.Where(c => c.Parent == parentId))
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
        try
        {
            var update = new UpdateDataModel
            {
                SurfaceId = surface.SurfaceId,
                Path = path,
                Value = value is not null
                    ? JsonSerializer.SerializeToElement(value, A2UiJsonSerializerContext.Default.String)
                    : null,
            };
            surface.DataModel.Apply(update);
            onDataModelChanged(surface.SurfaceId);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            if (logger is not null)
            {
                RendererLog.FailedToUpdateDataModel(logger, path, ex);
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
}
