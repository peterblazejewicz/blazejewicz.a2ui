using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Components;

namespace Blazejewicz.A2Ui.Avalonia.Catalog;

/// <summary>
/// Extension methods for <see cref="IRenderContext"/>.
/// </summary>
public static class RenderContextExtensions
{
    /// <summary>
    /// Checks whether all error-severity checks on the specified component pass.
    /// </summary>
    public static bool AreComponentChecksPassing(this IRenderContext context, string componentId)
    {
        if (context is RenderContext rc)
        {
            return rc.AreComponentChecksPassing(componentId);
        }

        return true;
    }

    /// <summary>
    /// Renders child controls paired with their layout weights.
    /// Children that render null are excluded so weights remain aligned with rendered controls.
    /// </summary>
    public static IReadOnlyList<(Control Control, double Weight)> RenderChildrenWithWeights(
        this IRenderContext context,
        string parentId
    )
    {
        if (context is RenderContext rc)
        {
            return rc.RenderChildrenWithWeights(parentId);
        }

        return context.RenderChildren(parentId).Select(c => (c, 0.0)).ToList();
    }

    /// <summary>
    /// Renders child controls paired with their layout weights for the given component.
    /// Children that render null are excluded so weights remain aligned with rendered controls.
    /// </summary>
    public static IReadOnlyList<(Control Control, double Weight)> RenderChildrenWithWeights(
        this IRenderContext context,
        A2UiComponent component
    )
    {
        if (context is RenderContext rc)
        {
            return rc.RenderChildrenWithWeights(component.Id);
        }

        if (component.Children?.Ids is { } ids)
        {
            var list = new List<(Control Control, double Weight)>(ids.Length);
            foreach (var id in ids)
            {
                var ctrl = context.RenderChild(id);
                if (ctrl is not null)
                {
                    double weight = context.GetComponentWeight(id) ?? 0;
                    list.Add((ctrl, weight));
                }
            }

            return list;
        }

        return context.RenderChildren(component.Id).Select(c => (c, 0.0)).ToList();
    }
}
