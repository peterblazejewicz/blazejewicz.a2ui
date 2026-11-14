using System.Text.Json;
using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Rpc;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Catalog;

/// <summary>Context passed to factory methods for cross-cutting concerns.</summary>
public interface IRenderContext
{
    /// <summary>Render a child component by ID.</summary>
    Control? RenderChild(string? childId);

    /// <summary>Render all children of a component.</summary>
    IEnumerable<Control> RenderChildren(string parentId);

    /// <summary>Fire a user action event back to the agent. SurfaceId is resolved automatically.</summary>
    void FireUserAction(string eventName, object? payload = null, string? componentId = null);

    /// <summary>Fire a user action event back to the agent with optional user message (v1.0).</summary>
    void FireUserAction(string eventName, object? payload, string? componentId, string? userMessage) =>
        this.FireUserAction(eventName, payload, componentId);

    /// <summary>Resolve a DynamicValue from the data model.</summary>
    string? Resolve(DynamicValue? value);

    /// <summary>
    /// Resolve a <see cref="DynamicValue"/> to its native <see cref="JsonElement"/> representation
    /// (preserving strings, numbers, booleans, arrays, and objects).
    /// </summary>
    JsonElement? ResolveJsonElement(DynamicValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Match<JsonElement?>(
            onString: s => JsonSerializer.SerializeToElement(s.Value, Core.A2UiJsonSerializerContext.Default.String),
            onNumber: n => Core.Surfaces.DataModel.SerializeNumberToElement(n.Value),
            onBool: b => JsonSerializer.SerializeToElement(b.Value, Core.A2UiJsonSerializerContext.Default.Boolean),
            onArray: a => a.Value.Clone(),
            onPath: _ =>
            {
                string? resolved = this.Resolve(value);
                return resolved is not null
                    ? JsonSerializer.SerializeToElement(resolved, Core.A2UiJsonSerializerContext.Default.String)
                    : Core.Surfaces.DataModel.NullElement;
            },
            onFunction: _ =>
            {
                string? resolved = this.Resolve(value);
                return resolved is not null
                    ? JsonSerializer.SerializeToElement(resolved, Core.A2UiJsonSerializerContext.Default.String)
                    : Core.Surfaces.DataModel.NullElement;
            },
            onObject: o =>
            {
                string? resolved = this.Resolve(o);
                if (resolved is not null)
                {
                    using var doc = JsonDocument.Parse(resolved);
                    return doc.RootElement.Clone();
                }

                return Core.Surfaces.DataModel.NullElement;
            }
        );
    }

    /// <summary>
    /// Resolve a <see cref="DynamicValue"/> to its native <see cref="JsonElement"/> representation
    /// under the specified <paramref name="callerRole"/>.
    /// </summary>
    JsonElement? ResolveJsonElement(DynamicValue? value, FunctionCallerRole callerRole) =>
        this.ResolveJsonElement(value);

    /// <summary>
    /// Invoke a catalog function from an active user gesture (e.g. a Button click action).
    /// Passes <see cref="FunctionCallerRole.RendererAction"/> so functions requiring
    /// user activation (such as <c>openUrl</c>) are permitted to execute.
    /// </summary>
    void InvokeActionFunction(FunctionCallValue functionCall) =>
        this.Resolve(DynamicValue.FromFunctionCall(functionCall));

    /// <summary>
    /// Write a string value back to the data model at the given JSON Pointer path.
    /// Used for two-way binding (e.g. TextField with value: {"path": "/username"}).
    /// </summary>
    void UpdateDataModel(string path, string? value);

    /// <summary>
    /// Write a typed JSON value (array, boolean, number, or string) back to the data model at the given JSON Pointer path.
    /// </summary>
    void UpdateDataModel(string path, JsonElement? value) =>
        this.UpdateDataModel(
            path,
            value is { ValueKind: JsonValueKind.String } s ? s.GetString() : value?.GetRawText()
        );

    /// <summary>
    /// Return the weight of a component by ID, or null if unknown / no weight set.
    /// Used by layout entries to build proportional star-sized grids.
    /// </summary>
    double? GetComponentWeight(string componentId);

    /// <summary>
    /// Optional logger for catalog entries and helpers that need diagnostics.
    /// May be null when logging is not configured.
    /// </summary>
    ILogger? Logger { get; }

    /// <summary>
    /// 0-based iteration index when evaluating template items within a collection scope, or null if outside collection scope.
    /// </summary>
    int? CollectionIndex => null;

    /// <summary>
    /// Creates a scoped child render context for collection iteration index.
    /// </summary>
    IRenderContext WithIndex(int index) => this;

    /// <summary>
    /// Optional RPC handler for dispatching remote agent functions.
    /// </summary>
    RpcHandler? RpcHandler => null;

    /// <summary>
    /// Report a renderer error (e.g. invalid action, action execution failure, or expression evaluation error)
    /// to be emitted to the agent.
    /// </summary>
    /// <param name="error">The structured error payload to report.</param>
    void ReportRendererError(RendererError error) { }

    /// <summary>
    /// Cancellation token tied to the surface/render lifetime.
    /// Use this for async operations (e.g. image downloads) so they are
    /// cancelled when the surface is cleared or replaced.
    /// </summary>
    CancellationToken SurfaceCancellation { get; }
}
