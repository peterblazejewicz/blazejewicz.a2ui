using System.Text.Json;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;

namespace Blazejewicz.A2Ui.Core.Validation;

/// <summary>
/// Parent-child composition constraints declared by a catalog component definition.
/// </summary>
public sealed record CatalogComponentConstraint
{
    /// <summary>Allowed parent component types. Virtual parent "Surface" represents the root container.</summary>
    public IReadOnlyList<string>? AllowedParents { get; init; }

    /// <summary>Allowed child component types.</summary>
    public IReadOnlyList<string>? AllowedChildren { get; init; }
}

/// <summary>
/// Schema definition for a catalog's component composition rules.
/// </summary>
public sealed class CatalogSchemaDefinition
{
    /// <summary>Catalog identifier.</summary>
    public string CatalogId { get; init; } = "";

    /// <summary>Mapping from component type name to composition constraints.</summary>
    public Dictionary<string, CatalogComponentConstraint> Components { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Parses a catalog schema definition from a JSON element.</summary>
    public static CatalogSchemaDefinition FromJson(JsonElement root)
    {
        var schema = new CatalogSchemaDefinition
        {
            CatalogId = root.TryGetProperty("catalogId", out var cId) ? (cId.GetString() ?? "") : "",
        };

        if (root.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Object)
        {
            foreach (var compProp in comps.EnumerateObject())
            {
                var compDef = compProp.Value;
                IReadOnlyList<string>? allowedParents = null;
                IReadOnlyList<string>? allowedChildren = null;

                if (compDef.TryGetProperty("allowedParents", out var ap) && ap.ValueKind == JsonValueKind.Array)
                {
                    allowedParents = [.. ap.EnumerateArray().Select(x => x.GetString()!)];
                }

                if (compDef.TryGetProperty("allowedChildren", out var ac) && ac.ValueKind == JsonValueKind.Array)
                {
                    allowedChildren = [.. ac.EnumerateArray().Select(x => x.GetString()!)];
                }

                if (allowedParents is not null || allowedChildren is not null)
                {
                    schema.Components[compProp.Name] = new CatalogComponentConstraint
                    {
                        AllowedParents = allowedParents,
                        AllowedChildren = allowedChildren,
                    };
                }
            }
        }

        return schema;
    }
}

/// <summary>
/// Validates component composition hierarchies against catalog allowedParents and allowedChildren constraints.
/// </summary>
public static class CompositionValidator
{
    /// <summary>
    /// Validates a list of components against the given component composition constraints.
    /// Returns a list of <see cref="RendererError"/> violations (UNALLOWED_PARENT or UNALLOWED_CHILD).
    /// </summary>
    public static IReadOnlyList<RendererError> Validate(
        string surfaceId,
        IReadOnlyList<A2UiComponent> components,
        IReadOnlyDictionary<string, CatalogComponentConstraint> constraints
    )
    {
        var errors = new List<RendererError>();
        var compMap = new Dictionary<string, (A2UiComponent Component, int Index)>(StringComparer.Ordinal);

        for (int i = 0; i < components.Count; i++)
        {
            compMap[components[i].Id] = (components[i], i);
        }

        // 1. Validate root component against implicit parent "Surface"
        if (compMap.TryGetValue("root", out var rootEntry))
        {
            var rootComp = rootEntry.Component;
            int rootIdx = rootEntry.Index;

            if (
                constraints.TryGetValue(rootComp.Component, out var rootConstraint)
                && rootConstraint.AllowedParents?.Contains("Surface") == false
            )
            {
                errors.Add(
                    new RendererError
                    {
                        Code = "UNALLOWED_PARENT",
                        SurfaceId = surfaceId,
                        Path = $"/components/{rootIdx}",
                        Message =
                            $"Root component '{rootComp.Id}' ({rootComp.Component}) cannot be placed under parent 'Surface'. Allowed parents: [{string.Join(", ", rootConstraint.AllowedParents.Select(p => $"'{p}'"))}].",
                    }
                );
            }
        }

        // 2. Validate parent-child edges
        for (int pIdx = 0; pIdx < components.Count; pIdx++)
        {
            var parent = components[pIdx];
            constraints.TryGetValue(parent.Component, out var parentConstraint);

            foreach (var edge in ExtractChildEdges(parent, pIdx))
            {
                if (!compMap.TryGetValue(edge.ChildId, out var childEntry))
                {
                    continue; // missing child referenced; handled by tree integrity checks
                }

                var child = childEntry.Component;
                int cIdx = childEntry.Index;

                constraints.TryGetValue(child.Component, out var childConstraint);

                // Check child's AllowedParents
                if (
                    childConstraint?.AllowedParents is not null
                    && !childConstraint.AllowedParents.Contains(parent.Component)
                )
                {
                    errors.Add(
                        new RendererError
                        {
                            Code = "UNALLOWED_PARENT",
                            SurfaceId = surfaceId,
                            Path = $"/components/{cIdx}",
                            Message =
                                $"Component '{child.Id}' ({child.Component}) cannot be placed under parent '{parent.Id}' ({parent.Component}). Allowed parents: [{string.Join(", ", childConstraint.AllowedParents.Select(p => $"'{p}'"))}].",
                        }
                    );
                }

                // Check parent's AllowedChildren
                if (
                    parentConstraint?.AllowedChildren is not null
                    && !parentConstraint.AllowedChildren.Contains(child.Component)
                )
                {
                    errors.Add(
                        new RendererError
                        {
                            Code = "UNALLOWED_CHILD",
                            SurfaceId = surfaceId,
                            Path = edge.Path,
                            Message = $"Component '{child.Component}' is not allowed inside '{parent.Component}'.",
                        }
                    );
                }
            }
        }

        return errors;
    }

    private sealed record ChildEdge(string ChildId, string Path);

    private static List<ChildEdge> ExtractChildEdges(A2UiComponent parent, int parentIndex)
    {
        var edges = new List<ChildEdge>();

        if (!string.IsNullOrEmpty(parent.Child))
        {
            edges.Add(new ChildEdge(parent.Child, $"/components/{parentIndex}/child"));
        }

        if (parent.Children is { } children)
        {
            if (children.Ids is { } ids)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    edges.Add(new ChildEdge(ids[i], $"/components/{parentIndex}/children/{i}"));
                }
            }
            else if (children.Template is { } tmpl)
            {
                edges.Add(new ChildEdge(tmpl.ComponentId, $"/components/{parentIndex}/children/template"));
            }
        }

        if (parent is TabsComponent tabsComp && tabsComp.Tabs is { } tabs)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                if (!string.IsNullOrEmpty(tabs[i].Child))
                {
                    edges.Add(new ChildEdge(tabs[i].Child, $"/components/{parentIndex}/tabs/{i}/child"));
                }
            }
        }

        if (parent is ModalComponent modal)
        {
            if (!string.IsNullOrEmpty(modal.Trigger))
            {
                edges.Add(new ChildEdge(modal.Trigger, $"/components/{parentIndex}/trigger"));
            }
            if (!string.IsNullOrEmpty(modal.Content))
            {
                edges.Add(new ChildEdge(modal.Content, $"/components/{parentIndex}/content"));
            }
        }

        return edges;
    }
}
