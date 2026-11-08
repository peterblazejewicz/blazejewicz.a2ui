using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog.Entries;

/// <summary>A2UI "Column" → vertical layout panel.</summary>
public sealed class ColumnCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Column";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ColumnComponent)component;
        var items = context.RenderChildrenWithWeights(component);
        return LayoutHelper.BuildLayout(
            Orientation.Vertical,
            items.Select(x => x.Control).ToList(),
            typed.Justify,
            typed.Align,
            items.Select(x => x.Weight).ToArray()
        );
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (ColumnComponent)component;
        var items = context.RenderChildrenWithWeights(component);
        return LayoutHelper.UpdateLayout(
            existing,
            Orientation.Vertical,
            items.Select(x => x.Control).ToList(),
            typed.Justify,
            typed.Align,
            items.Select(x => x.Weight).ToArray()
        );
    }
}

/// <summary>A2UI "Row" → horizontal layout panel.</summary>
public sealed class RowCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Row";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (RowComponent)component;
        var items = context.RenderChildrenWithWeights(component);
        return LayoutHelper.BuildLayout(
            Orientation.Horizontal,
            items.Select(x => x.Control).ToList(),
            typed.Justify,
            typed.Align,
            items.Select(x => x.Weight).ToArray()
        );
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var typed = (RowComponent)component;
        var items = context.RenderChildrenWithWeights(component);
        return LayoutHelper.UpdateLayout(
            existing,
            Orientation.Horizontal,
            items.Select(x => x.Control).ToList(),
            typed.Justify,
            typed.Align,
            items.Select(x => x.Weight).ToArray()
        );
    }
}

/// <summary>A2UI "Card" → Border with rounded corners and padding.</summary>
public sealed class CardCatalogEntry : ICatalogEntry
{
    /// <inheritdoc />
    public string ComponentType => "Card";

    /// <inheritdoc />
    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Classes = { "Card" },
        };

        // Card uses single child property
        if (component.Child is not null)
        {
            border.Child = context.RenderChild(component.Child);
        }
        else
        {
            // Fall back to Children / Parent lookup when Child is not specified
            var panel = new StackPanel { Spacing = 8 };
            foreach (var child in context.RenderChildren(component.Id))
            {
                panel.Children.Add(child);
            }

            border.Child = panel;
        }

        return border;
    }

    /// <inheritdoc />
    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context)
    {
        if (existing is not Border border)
        {
            return false;
        }

        if (component.Child is not null)
        {
            border.Child = context.RenderChild(component.Child);
            return true;
        }

        if (border.Child is StackPanel panel)
        {
            panel.Children.Clear();
            foreach (var child in context.RenderChildren(component.Id))
            {
                panel.Children.Add(child);
            }

            return true;
        }

        var newPanel = new StackPanel { Spacing = 8 };
        foreach (var child in context.RenderChildren(component.Id))
        {
            newPanel.Children.Add(child);
        }

        border.Child = newPanel;
        return true;
    }
}

internal static class LayoutHelper
{
    /// <summary>
    /// Build the appropriate layout panel for the given orientation and justify mode.
    /// When weights are provided and any weight > 0, uses a proportional star-sized Grid.
    /// Uses Grid for spaceBetween (distributes children with star-sized spacers),
    /// StackPanel for all other modes.
    /// </summary>
    public static Control BuildLayout(
        Orientation orientation,
        List<Control> children,
        string? justify,
        string? align,
        double[]? weights = null
    )
    {
        if (weights?.Any(w => w > 0) == true)
        {
            return BuildWeightedGrid(orientation, children, align, weights);
        }

        if (justify is "spaceBetween")
        {
            return BuildSpaceBetweenGrid(orientation, children, align);
        }

        var panel = new StackPanel { Orientation = orientation, Spacing = 8 };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        if (orientation == Orientation.Vertical)
        {
            panel.VerticalAlignment = MapMainAxis(justify);
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            panel.HorizontalAlignment = MapMainAxisH(justify);
            panel.VerticalAlignment = VerticalAlignment.Stretch;
        }

        ApplyCrossAxisAlignment(children, orientation, align);

        return panel;
    }

    /// <summary>
    /// Update an existing layout panel in place when the panel type matches.
    /// Returns true if updated in place, or false if the panel type changed and needs recreation.
    /// </summary>
    public static bool UpdateLayout(
        Control existing,
        Orientation orientation,
        List<Control> children,
        string? justify,
        string? align,
        double[]? weights = null
    )
    {
        bool hasWeights = weights?.Any(w => w > 0) == true;
        bool isSpaceBetween = !hasWeights && justify is "spaceBetween";

        if (hasWeights)
        {
            if (existing is not Grid grid)
            {
                return false;
            }

            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            PopulateWeightedGrid(grid, orientation, children, align, weights!);
            return true;
        }

        if (isSpaceBetween)
        {
            if (existing is not Grid grid)
            {
                return false;
            }

            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            PopulateSpaceBetweenGrid(grid, orientation, children, align);
            return true;
        }

        if (existing is not StackPanel panel || panel.Orientation != orientation)
        {
            return false;
        }

        panel.Children.Clear();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        if (orientation == Orientation.Vertical)
        {
            panel.VerticalAlignment = MapMainAxis(justify);
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            panel.HorizontalAlignment = MapMainAxisH(justify);
            panel.VerticalAlignment = VerticalAlignment.Stretch;
        }

        ApplyCrossAxisAlignment(children, orientation, align);
        return true;
    }

    private static Grid BuildWeightedGrid(
        Orientation orientation,
        List<Control> children,
        string? align,
        double[] weights
    )
    {
        var grid = new Grid();
        PopulateWeightedGrid(grid, orientation, children, align, weights);
        return grid;
    }

    private static void PopulateWeightedGrid(
        Grid grid,
        Orientation orientation,
        List<Control> children,
        string? align,
        double[] weights
    )
    {
        if (children.Count == 0)
        {
            return;
        }

        bool isHorizontal = orientation == Orientation.Horizontal;

        for (int i = 0; i < children.Count; i++)
        {
            double weight = i < weights.Length ? weights[i] : 0;
            var length = weight > 0 ? new GridLength(weight, GridUnitType.Star) : GridLength.Auto;

            if (isHorizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(length));
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(length));
            }

            var child = children[i];
            if (isHorizontal)
            {
                Grid.SetColumn(child, i);
            }
            else
            {
                Grid.SetRow(child, i);
            }

            grid.Children.Add(child);
        }

        ApplyCrossAxisAlignment(children, orientation, align);
    }

    private static Grid BuildSpaceBetweenGrid(Orientation orientation, List<Control> children, string? align)
    {
        var grid = new Grid();
        PopulateSpaceBetweenGrid(grid, orientation, children, align);
        return grid;
    }

    private static void PopulateSpaceBetweenGrid(Grid grid, Orientation orientation, List<Control> children, string? align)
    {
        if (children.Count == 0)
        {
            return;
        }

        bool isHorizontal = orientation == Orientation.Horizontal;

        for (int i = 0; i < children.Count; i++)
        {
            if (isHorizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var child = children[i];
            if (isHorizontal)
            {
                Grid.SetColumn(child, i * 2);
            }
            else
            {
                Grid.SetRow(child, i * 2);
            }

            grid.Children.Add(child);

            if (i < children.Count - 1)
            {
                if (isHorizontal)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                }
                else
                {
                    grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
                }
            }
        }

        ApplyCrossAxisAlignment(children, orientation, align);
    }

    private static void ApplyCrossAxisAlignment(List<Control> children, Orientation orientation, string? align)
    {
        if (align is null)
        {
            return;
        }

        foreach (var child in children)
        {
            if (orientation == Orientation.Horizontal)
            {
                child.VerticalAlignment = MapCrossAxisV(align);
            }
            else
            {
                child.HorizontalAlignment = MapCrossAxisH(align);
            }
        }
    }

    private static VerticalAlignment MapMainAxis(string? justify) =>
        justify switch
        {
            "start" => VerticalAlignment.Top,
            "center" => VerticalAlignment.Center,
            "end" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Stretch,
        };

    private static HorizontalAlignment MapMainAxisH(string? justify) =>
        justify switch
        {
            "start" => HorizontalAlignment.Left,
            "center" => HorizontalAlignment.Center,
            "end" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch,
        };

    private static VerticalAlignment MapCrossAxisV(string? align) =>
        align switch
        {
            "start" => VerticalAlignment.Top,
            "center" => VerticalAlignment.Center,
            "end" => VerticalAlignment.Bottom,
            "stretch" => VerticalAlignment.Stretch,
            _ => VerticalAlignment.Stretch,
        };

    private static HorizontalAlignment MapCrossAxisH(string? align) =>
        align switch
        {
            "start" => HorizontalAlignment.Left,
            "center" => HorizontalAlignment.Center,
            "end" => HorizontalAlignment.Right,
            "stretch" => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Stretch,
        };
}
