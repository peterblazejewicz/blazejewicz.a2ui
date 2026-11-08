using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core.Bindings;
using Blazejewicz.A2Ui.Core.Children;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Blazejewicz.A2Ui.Avalonia.Tests.Catalog;

public sealed class LayoutCatalogEntryTests
{
    // ── Row ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void RowCatalogEntry_Create_RendersChildrenHorizontally()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(
            dm,
            new TextBlock { Text = "Left" },
            new TextBlock { Text = "Right" }
        );

        var component = new RowComponent { Id = "row1", Children = ChildList.FromIds(["c1", "c2"]) };

        var control = entry.Create(component, dm, ctx);

        // Default layout is StackPanel for non-spaceBetween
        Assert.IsAssignableFrom<StackPanel>(control);
        var panel = (StackPanel)control;
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(2, panel.Children.Count);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_SpaceBetween_UsesGrid()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(
            dm,
            new TextBlock { Text = "Left" },
            new TextBlock { Text = "Right" }
        );

        var component = new RowComponent
        {
            Id = "row1",
            Children = ChildList.FromIds(["c1", "c2"]),
            Justify = "spaceBetween",
            Align = "center",
        };

        var control = entry.Create(component, dm, ctx);

        // spaceBetween uses a Grid with star spacers
        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.Children.Count);
        // 2 children -> 2 auto columns + 1 star spacer = 3 column definitions
        Assert.Equal(3, grid.ColumnDefinitions.Count);
        Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_Center_SetsHorizontalAlignmentCenter()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(dm, new TextBlock { Text = "Child" });

        var component = new RowComponent
        {
            Id = "row1",
            Children = ChildList.FromIds(["c1"]),
            Justify = "center",
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsAssignableFrom<StackPanel>(control);
        var panel = (StackPanel)control;
        Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment);
    }

    // ── Column ────────────────────────────────────────────────

    [AvaloniaFact]
    public void ColumnCatalogEntry_Create_RendersChildrenVertically()
    {
        var entry = new ColumnCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(
            dm,
            new TextBlock { Text = "Top" },
            new TextBlock { Text = "Bottom" }
        );

        var component = new ColumnComponent
        {
            Id = "col1",
            Children = ChildList.FromIds(["c1", "c2"]),
            Justify = "start",
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsAssignableFrom<StackPanel>(control);
        var panel = (StackPanel)control;
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.Equal(2, panel.Children.Count);
        Assert.Equal(VerticalAlignment.Top, panel.VerticalAlignment);
    }

    [AvaloniaFact]
    public void ColumnCatalogEntry_Create_SpaceBetween_UsesGrid()
    {
        var entry = new ColumnCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(
            dm,
            new TextBlock { Text = "Top" },
            new TextBlock { Text = "Bottom" }
        );

        var component = new ColumnComponent
        {
            Id = "col1",
            Children = ChildList.FromIds(["c1", "c2"]),
            Justify = "spaceBetween",
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.Children.Count);
        // Vertical spaceBetween -> row definitions
        Assert.Equal(3, grid.RowDefinitions.Count);
        Assert.True(grid.RowDefinitions[1].Height.IsStar);
    }

    // ── Cross-axis alignment ──────────────────────────────────

    [AvaloniaFact]
    public void RowCatalogEntry_Create_AlignCenter_SetsChildVerticalAlignmentCenter()
    {
        var child1 = new TextBlock { Text = "A" };
        var child2 = new TextBlock { Text = "B" };
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(dm, child1, child2);

        var component = new RowComponent
        {
            Id = "row1",
            Children = ChildList.FromIds(["c1", "c2"]),
            Align = "center",
        };

        entry.Create(component, dm, ctx);

        // Cross-axis for horizontal row is vertical
        Assert.Equal(VerticalAlignment.Center, child1.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, child2.VerticalAlignment);
    }

    [AvaloniaFact]
    public void ColumnCatalogEntry_Create_AlignCenter_SetsChildHorizontalAlignmentCenter()
    {
        var child1 = new TextBlock { Text = "A" };
        var child2 = new TextBlock { Text = "B" };
        var entry = new ColumnCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(dm, child1, child2);

        var component = new ColumnComponent
        {
            Id = "col1",
            Children = ChildList.FromIds(["c1", "c2"]),
            Align = "center",
        };

        entry.Create(component, dm, ctx);

        // Cross-axis for vertical column is horizontal
        Assert.Equal(HorizontalAlignment.Center, child1.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, child2.HorizontalAlignment);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_SpaceBetween_ZeroChildren_ReturnsEmptyGrid()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(dm); // no children

        var component = new RowComponent
        {
            Id = "row2",
            Children = ChildList.FromIds([]),
            Justify = "spaceBetween",
        };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Empty(grid.Children);
        Assert.Empty(grid.ColumnDefinitions);
    }

    // ── Weight (star-sized proportional layout) ───────────────

    [AvaloniaFact]
    public void RowCatalogEntry_Create_EqualWeights_UsesStarGrid()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new WeightedChildRenderContext(
            dm,
            new Dictionary<string, double> { ["c1"] = 1, ["c2"] = 1 },
            new TextBlock { Text = "Left" },
            new TextBlock { Text = "Right" }
        );

        var component = new RowComponent { Id = "row1", Children = ChildList.FromIds(["c1", "c2"]) };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.True(grid.ColumnDefinitions[0].Width.IsStar);
        Assert.Equal(1, grid.ColumnDefinitions[0].Width.Value);
        Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
        Assert.Equal(1, grid.ColumnDefinitions[1].Width.Value);
        Assert.Equal(2, grid.Children.Count);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_UnequalWeights_UsesProportionalStarColumns()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new WeightedChildRenderContext(
            dm,
            new Dictionary<string, double> { ["c1"] = 2, ["c2"] = 1 },
            new TextBlock { Text = "Wide" },
            new TextBlock { Text = "Narrow" }
        );

        var component = new RowComponent { Id = "row1", Children = ChildList.FromIds(["c1", "c2"]) };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(2, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(1, grid.ColumnDefinitions[1].Width.Value);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_MixedWeights_ZeroWeightGetsAuto()
    {
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new WeightedChildRenderContext(
            dm,
            new Dictionary<string, double> { ["c1"] = 0, ["c2"] = 1 },
            new TextBlock { Text = "Auto" },
            new TextBlock { Text = "Star" }
        );

        var component = new RowComponent { Id = "row1", Children = ChildList.FromIds(["c1", "c2"]) };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.True(grid.ColumnDefinitions[0].Width.IsAuto);
        Assert.True(grid.ColumnDefinitions[1].Width.IsStar);
    }

    [AvaloniaFact]
    public void ColumnCatalogEntry_Create_EqualWeights_UsesStarGridRows()
    {
        var entry = new ColumnCatalogEntry();
        var dm = new DataModel();
        var ctx = new WeightedChildRenderContext(
            dm,
            new Dictionary<string, double> { ["r1"] = 1, ["r2"] = 3 },
            new TextBlock { Text = "Top" },
            new TextBlock { Text = "Bottom" }
        );

        var component = new ColumnComponent { Id = "col1", Children = ChildList.FromIds(["r1", "r2"]) };

        var control = entry.Create(component, dm, ctx);

        Assert.IsType<Grid>(control);
        var grid = (Grid)control;
        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.True(grid.RowDefinitions[0].Height.IsStar);
        Assert.Equal(1, grid.RowDefinitions[0].Height.Value);
        Assert.True(grid.RowDefinitions[1].Height.IsStar);
        Assert.Equal(3, grid.RowDefinitions[1].Height.Value);
    }

    [AvaloniaFact]
    public void RowCatalogEntry_Create_NoWeights_FallsBackToStackPanel()
    {
        // When all weights are null/0, normal StackPanel layout is used
        var entry = new RowCatalogEntry();
        var dm = new DataModel();
        var ctx = new ChildReturningRenderContext(
            dm,
            new TextBlock { Text = "Left" },
            new TextBlock { Text = "Right" }
        );

        var component = new RowComponent { Id = "row1", Children = ChildList.FromIds(["c1", "c2"]) };

        var control = entry.Create(component, dm, ctx);

        Assert.IsAssignableFrom<StackPanel>(control);
    }

    // ── List listStyle ────────────────────────────────────────

    [AvaloniaFact]
    public void List_WithListStyleOrdered_RendersNumberedItems()
    {
        var entry = new ListCatalogEntry();
        var dm = new DataModel();
        var t1 = new TextBlock { Text = "First" };
        var t2 = new TextBlock { Text = "Second" };
        var ctx = new ChildReturningRenderContext(dm, t1, t2);

        var component = new ListComponent
        {
            Id = "list1",
            ListStyle = "ordered",
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        var control = entry.Create(component, dm, ctx);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(2, panel.Children.Count);

        var row1 = Assert.IsType<StackPanel>(panel.Children[0]);
        Assert.Equal(Orientation.Horizontal, row1.Orientation);
        Assert.Equal(6, row1.Spacing);
        var marker1 = Assert.IsType<TextBlock>(row1.Children[0]);
        Assert.Equal("1.", marker1.Text);
        Assert.Same(t1, row1.Children[1]);

        var row2 = Assert.IsType<StackPanel>(panel.Children[1]);
        Assert.Equal(Orientation.Horizontal, row2.Orientation);
        Assert.Equal(6, row2.Spacing);
        var marker2 = Assert.IsType<TextBlock>(row2.Children[0]);
        Assert.Equal("2.", marker2.Text);
        Assert.Same(t2, row2.Children[1]);
    }

    [AvaloniaFact]
    public void List_WithListStyleUnordered_RendersBulletedItems()
    {
        var entry = new ListCatalogEntry();
        var dm = new DataModel();
        var t1 = new TextBlock { Text = "Alpha" };
        var t2 = new TextBlock { Text = "Beta" };
        var ctx = new ChildReturningRenderContext(dm, t1, t2);

        var component = new ListComponent
        {
            Id = "list1",
            ListStyle = "unordered",
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        var control = entry.Create(component, dm, ctx);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(2, panel.Children.Count);

        var row1 = Assert.IsType<StackPanel>(panel.Children[0]);
        var marker1 = Assert.IsType<TextBlock>(row1.Children[0]);
        Assert.Equal("•", marker1.Text);
        Assert.Same(t1, row1.Children[1]);

        var row2 = Assert.IsType<StackPanel>(panel.Children[1]);
        var marker2 = Assert.IsType<TextBlock>(row2.Children[0]);
        Assert.Equal("•", marker2.Text);
        Assert.Same(t2, row2.Children[1]);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("none")]
    public void List_WithListStyleNoneOrNull_PreservesDirectChildren(string? listStyle)
    {
        var entry = new ListCatalogEntry();
        var dm = new DataModel();
        var t1 = new TextBlock { Text = "Item1" };
        var t2 = new TextBlock { Text = "Item2" };
        var ctx = new ChildReturningRenderContext(dm, t1, t2);

        var component = new ListComponent
        {
            Id = "list1",
            ListStyle = listStyle,
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        var control = entry.Create(component, dm, ctx);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(2, panel.Children.Count);
        Assert.Same(t1, panel.Children[0]);
        Assert.Same(t2, panel.Children[1]);
    }

    [AvaloniaFact]
    public void List_WithHorizontalDirection_IgnoresListStyle()
    {
        var entry = new ListCatalogEntry();
        var dm = new DataModel();
        var t1 = new TextBlock { Text = "Item1" };
        var ctx = new ChildReturningRenderContext(dm, t1);

        var component = new ListComponent
        {
            Id = "list1",
            Direction = "horizontal",
            ListStyle = "ordered",
            Children = ChildList.FromIds(["c1"]),
        };

        var control = entry.Create(component, dm, ctx);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Single(panel.Children);
        Assert.Same(t1, panel.Children[0]);
    }

    [AvaloniaFact]
    public void List_Update_WithListStyleChange_UpdatesMarkers()
    {
        var entry = new ListCatalogEntry();
        var dm = new DataModel();
        var t1 = new TextBlock { Text = "Item1" };
        var t2 = new TextBlock { Text = "Item2" };
        var ctx = new ChildReturningRenderContext(dm, t1, t2);

        var compUnordered = new ListComponent
        {
            Id = "list1",
            ListStyle = "unordered",
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        var control = entry.Create(compUnordered, dm, ctx);
        var sv = Assert.IsType<ScrollViewer>(control);
        var panel = Assert.IsType<StackPanel>(sv.Content);
        Assert.Equal(2, panel.Children.Count);
        var rowUnordered = Assert.IsType<StackPanel>(panel.Children[0]);
        var markerUnordered = Assert.IsType<TextBlock>(rowUnordered.Children[0]);
        Assert.Equal("•", markerUnordered.Text);

        // Update to ordered
        var t1Ordered = new TextBlock { Text = "Item1" };
        var t2Ordered = new TextBlock { Text = "Item2" };
        var ctxOrdered = new ChildReturningRenderContext(dm, t1Ordered, t2Ordered);

        var compOrdered = new ListComponent
        {
            Id = "list1",
            ListStyle = "ordered",
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        bool updated = entry.Update(control, compOrdered, dm, ctxOrdered);
        Assert.True(updated);
        Assert.Equal(2, panel.Children.Count);
        var rowOrdered1 = Assert.IsType<StackPanel>(panel.Children[0]);
        var markerOrdered1 = Assert.IsType<TextBlock>(rowOrdered1.Children[0]);
        Assert.Equal("1.", markerOrdered1.Text);
        var rowOrdered2 = Assert.IsType<StackPanel>(panel.Children[1]);
        var markerOrdered2 = Assert.IsType<TextBlock>(rowOrdered2.Children[0]);
        Assert.Equal("2.", markerOrdered2.Text);

        // Update to none
        var t1None = new TextBlock { Text = "Item1" };
        var t2None = new TextBlock { Text = "Item2" };
        var ctxNone = new ChildReturningRenderContext(dm, t1None, t2None);

        var compNone = new ListComponent
        {
            Id = "list1",
            ListStyle = "none",
            Children = ChildList.FromIds(["c1", "c2"]),
        };

        bool updatedToNone = entry.Update(control, compNone, dm, ctxNone);
        Assert.True(updatedToNone);
        Assert.Equal(2, panel.Children.Count);
        Assert.Same(t1None, panel.Children[0]);
        Assert.Same(t2None, panel.Children[1]);
    }
}

/// <summary>
/// A render context that returns pre-built child controls in order,
/// allowing layout entry tests without a full Surface/Renderer pipeline.
/// </summary>
internal sealed class ChildReturningRenderContext(DataModel dm, params Control[] children) : IRenderContext
{
    private readonly DataModel _dm = dm;
    private readonly Queue<Control> _children = new(children);

    public Control? RenderChild(string? childId) => this._children.Count > 0 ? this._children.Dequeue() : null;

    public IEnumerable<Control> RenderChildren(string parentId)
    {
        while (this._children.Count > 0)
        {
            yield return this._children.Dequeue();
        }
    }

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) { }

    public string? Resolve(DynamicValue? value) => this._dm.Resolve(value);

    public void UpdateDataModel(string path, string? value) { }

    public double? GetComponentWeight(string componentId) => null;

    public ILogger? Logger => null;

    public CancellationToken SurfaceCancellation => CancellationToken.None;
}

/// <summary>
/// A render context with configurable per-ID weight values for testing weighted layouts.
/// </summary>
internal sealed class WeightedChildRenderContext(DataModel dm, Dictionary<string, double> weights, params Control[] children) : IRenderContext
{
    private readonly DataModel _dm = dm;
    private readonly Queue<Control> _children = new(children);
    private readonly Dictionary<string, double> _weights = weights;

    public Control? RenderChild(string? childId) => this._children.Count > 0 ? this._children.Dequeue() : null;

    public IEnumerable<Control> RenderChildren(string parentId)
    {
        while (this._children.Count > 0)
        {
            yield return this._children.Dequeue();
        }
    }

    public void FireUserAction(string eventName, object? payload = null, string? componentId = null) { }

    public string? Resolve(DynamicValue? value) => this._dm.Resolve(value);

    public void UpdateDataModel(string path, string? value) { }

    public double? GetComponentWeight(string componentId) =>
        this._weights.TryGetValue(componentId, out var w) ? w : null;

    public ILogger? Logger => null;

    public CancellationToken SurfaceCancellation => CancellationToken.None;
}
