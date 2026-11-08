using Avalonia.Controls;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Avalonia.Catalog;

/// <summary>
/// Factory contract for a single A2UI component type.
/// Implement this for each type you add to the catalog.
/// </summary>
public interface ICatalogEntry
{
    /// <summary>
    /// A2UI component type string, e.g. "Text", "Button", "Column".
    /// Must exactly match the value in A2UiComponent.Component.
    /// </summary>
    string ComponentType { get; }

    /// <summary>
    /// Create an Avalonia control for this component.
    /// Called on the UI thread (Dispatcher).
    /// </summary>
    Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context);

    /// <summary>
    /// Update an existing control in-place (avoids full recreate).
    /// Return false to signal the renderer should recreate instead.
    /// </summary>
    bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context);
}
