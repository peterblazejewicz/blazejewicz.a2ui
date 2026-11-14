using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Blazejewicz.A2Ui.Avalonia.Catalog.Entries;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Avalonia.Catalog;

/// <summary>
/// Registry of A2UI type → control factory mappings.
/// Supports single and multi-catalog resolution across v0.9 and v1.0.
/// </summary>
public sealed class CatalogRegistry(ILogger<CatalogRegistry>? logger = null)
{
    private readonly Dictionary<string, ICatalogEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogRegistry> _catalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _catalogVersions = new(StringComparer.Ordinal);
    private readonly ILogger<CatalogRegistry> _logger = logger ?? NullLogger<CatalogRegistry>.Instance;

    /// <summary>Identifier for this catalog (default: Basic Catalog v1.0).</summary>
    public string CatalogId { get; set; } = A2UiProtocol.BasicCatalogIdV10;

    /// <summary>Protocol specification version for this catalog (default: "1.0").</summary>
    public string ProtocolVersion { get; set; } = A2UiProtocol.Version10;

    /// <summary>Register a catalog entry. Throws if already registered.</summary>
    public CatalogRegistry Register(ICatalogEntry entry)
    {
        this._entries.Add(entry.ComponentType, entry);
        CatalogRegistryLog.CatalogEntryRegistered(this._logger, entry.ComponentType, entry.GetType().Name);
        return this;
    }

    /// <summary>Register a simple factory function without implementing ICatalogEntry.</summary>
    public CatalogRegistry Register(
        string componentType,
        Func<A2UiComponent, DataModel, IRenderContext, Control> factory
    )
    {
        return this.Register(new DelegateCatalogEntry(componentType, factory));
    }

    /// <summary>Register a separate sub-catalog by catalog ID.</summary>
    public CatalogRegistry RegisterCatalog(
        string catalogId,
        CatalogRegistry catalog,
        string protocolVersion = A2UiProtocol.Version10
    )
    {
        this._catalogs[catalogId] = catalog;
        this._catalogVersions[catalogId] = protocolVersion;
        return this;
    }

    /// <summary>Register a catalog entry under a specific catalog ID.</summary>
    public CatalogRegistry Register(string catalogId, ICatalogEntry entry)
    {
        if (!this._catalogs.TryGetValue(catalogId, out var subCatalog))
        {
            subCatalog = new CatalogRegistry(this._logger) { CatalogId = catalogId };
            this._catalogs[catalogId] = subCatalog;
            this._catalogVersions[catalogId] = A2UiProtocol.Version10;
        }

        subCatalog.Register(entry);
        return this;
    }

    /// <summary>Looks up a catalog entry by component type string in the primary catalog.</summary>
    public bool TryGetEntry(string componentType, [NotNullWhen(true)] out ICatalogEntry? entry)
    {
        if (this._entries.TryGetValue(componentType, out entry))
        {
            return true;
        }

        CatalogRegistryLog.CatalogLookupMiss(this._logger, componentType, string.Join(",", this._entries.Keys));
        entry = null;
        return false;
    }

    /// <summary>
    /// Checks whether a catalog ID is known (either built-in or explicitly registered).
    /// </summary>
    public bool IsKnownCatalog(string? catalogId)
    {
        if (string.IsNullOrEmpty(catalogId))
        {
            return true;
        }

        return this._catalogs.ContainsKey(catalogId)
            || IsBuiltInCatalog(catalogId)
            || catalogId == this.CatalogId;
    }

    private static bool IsBuiltInCatalog(string? catalogId)
    {
        if (string.IsNullOrEmpty(catalogId))
        {
            return true;
        }

        return catalogId is "basic"
            or "minimal"
            or A2UiProtocol.BasicCatalogIdV10
            or A2UiProtocol.BasicCatalogIdV091
            or A2UiProtocol.BasicCatalogIdV09
            || catalogId.Contains("specification/v1_0/", StringComparison.Ordinal)
            || catalogId.Contains("specification/v0_9/", StringComparison.Ordinal)
            || catalogId.Contains("specification/v0_9_1/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a catalog entry per v1.0 multi-catalog resolution rules:
    /// explicit component catalogId first, then surface default catalogId, falling back to primary entries.
    /// </summary>
    public bool TryResolveEntry(
        string? componentCatalogId,
        string? surfaceDefaultCatalogId,
        string componentType,
        [NotNullWhen(true)] out ICatalogEntry? entry
    )
    {
        string? catId = !string.IsNullOrEmpty(componentCatalogId) ? componentCatalogId : surfaceDefaultCatalogId;

        if (!string.IsNullOrEmpty(catId))
        {
            if (
                this._catalogs.TryGetValue(catId, out var subCatalog)
                && subCatalog.TryGetEntry(componentType, out entry)
            )
            {
                return true;
            }

            if (IsBuiltInCatalog(catId) || catId == this.CatalogId)
            {
                return this.TryGetEntry(componentType, out entry);
            }

            // If no sub-catalogs have been explicitly registered, allow fallback to primary entries
            if (this._catalogs.Count == 0)
            {
                return this.TryGetEntry(componentType, out entry);
            }

            // Unknown catalog ID must NOT fall back to primary entries when sub-catalogs are registered
            entry = null;
            return false;
        }

        return this.TryGetEntry(componentType, out entry);
    }

    /// <summary>
    /// Resolves a catalog entry for a component on a surface, strictly validating
    /// catalog support and protocol version matching per v1.0 rules.
    /// </summary>
    public bool TryResolveEntry(
        Surface surface,
        A2UiComponent component,
        [NotNullWhen(true)] out ICatalogEntry? entry
    )
    {
        string? catId = !string.IsNullOrEmpty(component.CatalogId) ? component.CatalogId : surface.CatalogId;

        if (surface.SupportedCatalogIds is not null && !string.IsNullOrEmpty(catId) && !surface.SupportedCatalogIds.Contains(catId))
        {
            entry = null;
            return false;
        }

        bool isV10 = surface.ProtocolVersion == A2UiProtocol.Version10
            || (surface.ProtocolVersion is null && (this._catalogs.Count > 0 || catId?.Contains("v1_0", StringComparison.Ordinal) == true));
        if (isV10)
        {
            if (string.IsNullOrEmpty(catId))
            {
                if (this._catalogs.Count > 0)
                {
                    entry = null;
                    return false;
                }

                return this.TryGetEntry(component.Component, out entry);
            }

            if (this._catalogVersions.TryGetValue(catId, out var ver) && ver != A2UiProtocol.Version10)
            {
                entry = null;
                return false;
            }

            if (this._catalogs.TryGetValue(catId, out var subCatalog))
            {
                return subCatalog.TryGetEntry(component.Component, out entry);
            }

            if (IsBuiltInCatalog(catId) || catId == this.CatalogId)
            {
                return this.TryGetEntry(component.Component, out entry);
            }

            entry = null;
            return false;
        }

        return this.TryResolveEntry(component.CatalogId, surface.DefaultCatalogId, component.Component, out entry);
    }

    /// <summary>
    /// Resolves a catalog entry for a component on a surface, strictly validating
    /// catalog support and protocol version matching per v1.0 rules.
    /// </summary>
    /// <exception cref="A2UiCatalogException">Thrown when catalog resolution or version constraints fail.</exception>
    public ICatalogEntry ResolveEntry(Surface surface, A2UiComponent component)
    {
        string? catId = !string.IsNullOrEmpty(component.CatalogId) ? component.CatalogId : surface.CatalogId;

        if (surface.SupportedCatalogIds is not null && !string.IsNullOrEmpty(catId) && !surface.SupportedCatalogIds.Contains(catId))
        {
            throw new A2UiCatalogException($"Catalog '{catId}' is not supported by surface '{surface.SurfaceId}'.");
        }

        bool isV10 = surface.ProtocolVersion == A2UiProtocol.Version10
            || (surface.ProtocolVersion is null && (this._catalogs.Count > 0 || catId?.Contains("v1_0", StringComparison.Ordinal) == true));
        if (isV10)
        {
            if (string.IsNullOrEmpty(catId))
            {
                if (this._catalogs.Count == 0 && this.TryGetEntry(component.Component, out var defaultFallback) && defaultFallback is not null)
                {
                    return defaultFallback;
                }

                throw new A2UiCatalogException(
                    $"Component '{component.Id}' does not specify a catalogId and surface '{surface.SurfaceId}' has no default catalogId."
                );
            }

            if (this._catalogVersions.TryGetValue(catId, out var ver) && ver != A2UiProtocol.Version10)
            {
                throw new A2UiCatalogException(
                    $"Protocol version mismatch: cannot mix catalog '{catId}' ({ver}) with surface version {A2UiProtocol.Version10}."
                );
            }

            if (this._catalogs.TryGetValue(catId, out var subCatalog))
            {
                if (subCatalog.TryGetEntry(component.Component, out var entry) && entry is not null)
                {
                    return entry;
                }

                throw new A2UiCatalogException(
                    $"Component type '{component.Component}' was not found in catalog '{catId}'."
                );
            }

            if (IsBuiltInCatalog(catId) || catId == this.CatalogId)
            {
                if (this.TryGetEntry(component.Component, out var entry) && entry is not null)
                {
                    return entry;
                }

                throw new A2UiCatalogException(
                    $"Component type '{component.Component}' was not found in catalog '{catId}'."
                );
            }

            throw new A2UiCatalogException($"Catalog '{catId}' is not supported by surface '{surface.SurfaceId}'.");
        }

        if (this.TryResolveEntry(component.CatalogId, surface.DefaultCatalogId, component.Component, out var legacyEntry))
        {
            return legacyEntry;
        }

        throw new A2UiCatalogException(
            $"Component type '{component.Component}' was not found in catalog '{catId ?? "(default)"}'."
        );
    }

    /// <summary>Gets the set of registered component type strings in the primary catalog.</summary>
    public IReadOnlyCollection<string> RegisteredTypes => this._entries.Keys;

    /// <summary>
    /// Builds the default catalog with all 18 Basic Catalog component types.
    /// </summary>
    public static CatalogRegistry CreateDefault(
        ILoggerFactory? loggerFactory = null,
        A2UiAvaloniaOptions? options = null
    )
    {
        return new CatalogRegistry(loggerFactory?.CreateLogger<CatalogRegistry>())
            // Display
            .Register(new TextCatalogEntry())
            .Register(new ImageCatalogEntry(loggerFactory?.CreateLogger<ImageCatalogEntry>(), options?.HttpClient, options))
            .Register(new IconCatalogEntry())
            .Register(new VideoCatalogEntry(options))
            .Register(new AudioPlayerCatalogEntry(options))
            .Register(new DividerCatalogEntry())
            // Layout
            .Register(new RowCatalogEntry())
            .Register(new ColumnCatalogEntry())
            .Register(new ListCatalogEntry())
            .Register(new CardCatalogEntry())
            .Register(new TabsCatalogEntry())
            .Register(new ModalCatalogEntry())
            // Interactive
            .Register(new ButtonCatalogEntry())
            .Register(new TextFieldCatalogEntry())
            .Register(new CheckBoxCatalogEntry())
            .Register(new ChoicePickerCatalogEntry())
            .Register(new DateTimeInputCatalogEntry())
            .Register(new SliderCatalogEntry());
    }
}

internal sealed class DelegateCatalogEntry(
    string componentType,
    Func<A2UiComponent, DataModel, IRenderContext, Control> factory
) : ICatalogEntry
{
    public string ComponentType => componentType;

    public Control Create(A2UiComponent component, DataModel dataModel, IRenderContext context) =>
        factory(component, dataModel, context);

    public bool Update(Control existing, A2UiComponent component, DataModel dataModel, IRenderContext context) => false;
}

internal static partial class CatalogRegistryLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Catalog entry registered: {ComponentType} → {EntryTypeName}"
    )]
    public static partial void CatalogEntryRegistered(ILogger logger, string componentType, string entryTypeName);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Catalog lookup miss for component type '{ComponentType}'. Known types: [{KnownTypes}]"
    )]
    public static partial void CatalogLookupMiss(ILogger logger, string componentType, string knownTypes);
}
