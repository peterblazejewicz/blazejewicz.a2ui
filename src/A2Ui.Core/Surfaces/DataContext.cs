namespace Blazejewicz.A2Ui.Core.Surfaces;

/// <summary>
/// Execution and data context for evaluating data bindings and function calls on an A2UI surface.
/// </summary>
public sealed record DataContext
{
    /// <summary>The surface data model.</summary>
    public DataModel DataModel { get; init; } = new();

    /// <summary>Optional base JSON pointer path for relative scoping.</summary>
    public string? BasePath { get; init; }

    /// <summary>0-based iteration index when evaluating template items within a collection scope.</summary>
    public int? CollectionIndex { get; init; }

    /// <summary>Creates a copy of this context with the specified 0-based collection index.</summary>
    public DataContext WithIndex(int index) => this with { CollectionIndex = index };

    /// <summary>Creates a copy of this context with the specified base path.</summary>
    public DataContext WithBasePath(string? basePath) => this with { BasePath = basePath };
}
