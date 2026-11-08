using System.Text.Json;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Validation;

namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Represents a live A2UI surface with its component tree and data model.
/// </summary>
/// <param name="surfaceId">Unique identifier for this surface.</param>
/// <param name="catalogId">Optional catalog identifier defining the default component set.</param>
public sealed class Surface(string surfaceId, string? catalogId = null)
{
    /// <summary>Unique identifier for this surface.</summary>
    public string SurfaceId { get; } = surfaceId;

    /// <summary>Catalog identifier defining the default allowed component set for this surface.</summary>
    public string? CatalogId { get; } = catalogId;

    /// <summary>Protocol specification version of this surface ("1.0", "v0.9.1", etc.).</summary>
    public string? ProtocolVersion { get; set; }

    /// <summary>Default catalog ID for this surface (alias for <see cref="CatalogId"/>).</summary>
    public string? DefaultCatalogId => this.CatalogId;

    /// <summary>Optional list of all catalog IDs supported on this surface for multi-catalog resolution.</summary>
    public IReadOnlyList<string>? SupportedCatalogIds { get; set; }

    /// <summary>Optional component composition constraints defined by the catalog.</summary>
    public IReadOnlyDictionary<string, CatalogComponentConstraint>? ComponentConstraints { get; set; }

    /// <summary>Optional surface-level metadata.</summary>
    public SurfaceMetadata? Metadata { get; internal set; }

    /// <summary>Per-surface data model store for two-way binding.</summary>
    public DataModel DataModel { get; } = new();

    /// <summary>Optional theme configuration from the createSurface message.</summary>
    public JsonElement? Theme { get; internal set; }

    /// <summary>Whether the client should send data model state back to the server.</summary>
    public bool SendDataModel { get; internal set; }

    private readonly Dictionary<string, A2UiComponent> _components = [];

    /// <summary>Read-only view of the component tree keyed by component ID.</summary>
    public IReadOnlyDictionary<string, A2UiComponent> Components => this._components;

    /// <summary>Adds or replaces components in the surface's component tree.</summary>
    /// <param name="components">Components to upsert.</param>
    public void UpdateComponents(A2UiComponent[] components)
    {
        foreach (var c in components)
        {
            this._components[c.Id] = c;
        }
    }

    /// <summary>
    /// Gets the root components of the surface: prefers the component with <c>id == "root"</c>,
    /// otherwise components not referenced as children by any other component, or components with a <see langword="null"/> <see cref="A2UiComponent.Parent"/>.
    /// </summary>
    public IEnumerable<A2UiComponent> GetRootComponents()
    {
        if (this._components.TryGetValue("root", out var rootComp))
        {
            return [rootComp];
        }

        var childIds = new HashSet<string>();
        foreach (var c in this._components.Values)
        {
            if (c.Child is not null)
            {
                childIds.Add(c.Child);
            }

            if (c.Children?.Ids is { } ids)
            {
                foreach (var id in ids)
                {
                    childIds.Add(id);
                }
            }

            if (c.Children?.Template is { } tmpl)
            {
                childIds.Add(tmpl.ComponentId);
            }

            if (c is ModalComponent modal)
            {
                if (modal.Trigger is not null)
                {
                    childIds.Add(modal.Trigger);
                }

                if (modal.Content is not null)
                {
                    childIds.Add(modal.Content);
                }
            }
            if (c is TabsComponent tabsComp && tabsComp.Tabs is { } tabs)
            {
                foreach (var tab in tabs)
                {
                    childIds.Add(tab.Child);
                }
            }
        }

        var roots = this._components.Values.Where(c => !childIds.Contains(c.Id)).ToList();
        if (roots.Count > 0)
        {
            return roots;
        }

        return this._components.Values.Where(c => c.Parent is null);
    }
}
