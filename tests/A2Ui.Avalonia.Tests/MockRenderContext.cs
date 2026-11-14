using System.Text.Json;
using Avalonia.Controls;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Tests;

internal sealed class MockRenderContext(DataModel dataModel) : IRenderContext
{
    public List<(string EventName, object? Payload, string? ComponentId)> FiredActions { get; } = [];
    public List<(string Path, string? Value)> DataModelUpdates { get; } = [];

    public Control? RenderChild(string? childId) => null;

    public IEnumerable<Control> RenderChildren(string parentId) => [];

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) =>
        this.FiredActions.Add((eventName, payload, componentId));

    public string? Resolve(DynamicValue? value) => dataModel.Resolve(value);

    public JsonElement? ResolveJsonElement(DynamicValue? value) => dataModel.ResolveJsonElement(value);

    public void UpdateDataModel(string path, string? value)
    {
        this.DataModelUpdates.Add((path, value));
        dataModel.Apply(path, value is not null ? JsonSerializer.SerializeToElement(value) : null);
    }

    public void UpdateDataModel(string path, JsonElement? value)
    {
        string? recorded = value is { ValueKind: JsonValueKind.String } s ? s.GetString() : value?.GetRawText();
        this.DataModelUpdates.Add((path, recorded));
        dataModel.Apply(path, value);
    }

    public double? GetComponentWeight(string componentId) => null;

    public ILogger? Logger => null;

    public CancellationToken SurfaceCancellation { get; init; }
}
