using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Bindings;
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
    /// Write a value back to the data model at the given JSON Pointer path.
    /// Used for two-way binding (e.g. TextField with value: {"path": "/username"}).
    /// </summary>
    void UpdateDataModel(string path, string? value);

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
    /// Cancellation token tied to the surface/render lifetime.
    /// Use this for async operations (e.g. image downloads) so they are
    /// cancelled when the surface is cleared or replaced.
    /// </summary>
    CancellationToken SurfaceCancellation { get; }
}
