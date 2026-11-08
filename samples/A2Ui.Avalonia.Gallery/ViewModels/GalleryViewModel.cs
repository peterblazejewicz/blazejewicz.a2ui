using System.Collections.ObjectModel;
using System.Text.Json;
using Blazejewicz.A2Ui.Avalonia.Gallery.Models;
using Blazejewicz.A2Ui.Avalonia.Gallery.Services;
using Blazejewicz.A2Ui.Core;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Gallery.ViewModels;

public sealed partial class GalleryViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions s_indentedJson = new() { WriteIndented = true };

    private readonly SurfaceManager _manager;
    private readonly GalleryDataLoader _dataLoader;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly List<DemoItem> _allItems = [];

    public GalleryViewModel(SurfaceManager manager, GalleryDataLoader dataLoader, ILogger<GalleryViewModel> logger)
    {
        this._manager = manager;
        this._dataLoader = dataLoader;
        this._logger = logger;

        this._manager.SurfaceCreated += this.OnSurfaceCreated;
        this._manager.SurfaceDeleted += this.OnSurfaceDeleted;
        this._manager.ComponentsUpdated += this.OnComponentsUpdated;
        this._manager.DataModelUpdated += this.OnDataModelUpdated;
    }

    // ── State ──────────────────────────────────────────────

    [ObservableProperty]
    private string _selectedProtocolVersion = "v1.0";

    public ObservableCollection<string> AvailableProtocolVersions { get; } = ["v1.0", "v0.9.1"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalMessageCount))]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private DemoItem? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(SurfacePlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(StepOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepAllCommand))]
    private int _processedMessageCount;

    [ObservableProperty]
    private string _currentDataModelJson = "{}";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SurfacePlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private Surface? _activeSurface;

    /// <summary>
    /// Tracks whether the active surface has renderable components.
    /// Set to true when ComponentsUpdated fires; reset on surface change.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private bool _hasComponents;

    public ObservableCollection<DemoItem> DemoItems { get; } = [];
    public ObservableCollection<string> ActionLogs { get; } = [];

    // ── Computed ───────────────────────────────────────────

    public int TotalMessageCount => this.SelectedItem?.MessageCount ?? 0;
    public bool CanAdvance =>
        this.SelectedItem is not null && this.ProcessedMessageCount < this.SelectedItem.MessageCount;

    /// <summary>
    /// True when the placeholder text should be visible instead of the rendered surface.
    /// Covers three states: no messages processed yet, surface created but no components
    /// received yet (between createSurface and first updateComponents), and no surface at all.
    /// </summary>
    public bool ShowPlaceholder => this.ActiveSurface is null || !this.HasComponents;

    /// <summary>
    /// Placeholder text shown in the surface area.
    /// </summary>
    public string SurfacePlaceholder =>
        this.ProcessedMessageCount == 0
            ? "Surface not initialized. Click '+1 Message' to begin."
            : "Loading surface...";

    // ── Commands ──────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void StepOne() => this.AdvanceMessages(count: 1);

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void StepAll() => this.AdvanceMessages(all: true);

    [RelayCommand]
    private void Reset() => this.ResetSurface();

    // ── View event ────────────────────────────────────────

    public event EventHandler? SurfaceRefreshRequested;

    // ── Initialization ────────────────────────────────────

    partial void OnSelectedProtocolVersionChanged(string value)
    {
        _ = value;
        this.FilterDemoItems();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        this.IsLoading = true;
        try
        {
            IReadOnlyList<DemoItem> items = await this._dataLoader.LoadAsync(ct).ConfigureAwait(true);
            this._allItems.Clear();
            this._allItems.AddRange(items);
            this.FilterDemoItems();
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    private void FilterDemoItems()
    {
        this.ResetSurface();
        this.DemoItems.Clear();

        var matched = this._allItems.Where(item => item.ProtocolVersion == this.SelectedProtocolVersion).ToList();

        for (int i = 0; i < matched.Count; i++)
        {
            this.DemoItems.Add(matched[i] with { Title = $"{i + 1}. {StripLeadingNumber(matched[i].Title)}" });
        }

        this.SelectedItem = this.DemoItems.Count > 0 ? this.DemoItems[0] : null;
    }

    private static string StripLeadingNumber(string title)
    {
        int dot = title.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && int.TryParse(title[..dot], out _))
        {
            return title[(dot + 2)..];
        }

        return title;
    }

    // ── Item selection ────────────────────────────────────

    partial void OnSelectedItemChanged(DemoItem? value)
    {
        if (value is null)
        {
            return;
        }

        this.ResetSurface();
        this.AdvanceMessages(all: true);
    }

    // ── Message processing ────────────────────────────────

    private void AdvanceMessages(bool all = false, int count = 0)
    {
        DemoItem? item = this.SelectedItem;
        if (item is null)
        {
            return;
        }

        int start = this.ProcessedMessageCount;
        int end = all ? item.MessageCount : Math.Min(start + count, item.MessageCount);

        for (int i = start; i < end; i++)
        {
            try
            {
                if (item.RawJsonMessages is { Count: > 0 } rawList && i < rawList.Count)
                {
                    this._manager.Process(rawList[i]);
                }
                else
                {
                    this._manager.Process(item.Messages[i]);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or A2UiMessageValidationException)
            {
                this.ActionLogs.Insert(0, $"[Error] Failed to process message {i}: {ex.Message}");
            }
            catch (Exception ex)
            {
                this.ActionLogs.Insert(
                    0,
                    $"[Error] Unexpected failure processing message {i}: {ex.GetType().Name}: {ex.Message}"
                );
                this._logger.LogError(ex, "Unexpected exception processing message {Index}", i);
            }
        }

        this.ProcessedMessageCount = end;
    }

    private void ResetSurface()
    {
        DemoItem? item = this.SelectedItem;
        if (item is null)
        {
            return;
        }

        if (this._manager.GetSurface(item.Id) is not null)
        {
            if (item.ProtocolVersion is A2UiProtocol.Version10 or "v1.0")
            {
                this._manager.Process(
                    new AgentToRendererMessage
                    {
                        Version = A2UiProtocol.Version10,
                        DeleteSurface = new DeleteSurface { SurfaceId = item.Id },
                    }
                );
            }
            else
            {
                this._manager.Process(
                    new A2UiMessage
                    {
                        Version = A2UiProtocol.Version09,
                        DeleteSurface = new DeleteSurface { SurfaceId = item.Id },
                    }
                );
            }
        }

        this.ProcessedMessageCount = 0;
        this.CurrentDataModelJson = "{}";
        this.ActionLogs.Clear();
        this.HasComponents = false;
        this.ActiveSurface = null;
    }

    /// <summary>
    /// Refresh the data model JSON display from the active surface.
    /// Called when client-side two-way bindings update the data model.
    /// </summary>
    public void RefreshDataModelJson()
    {
        if (this.ActiveSurface is { } surface)
        {
            this.UpdateDataModelJson(surface);
        }
    }

    // ── Action logging ────────────────────────────────────

    public void LogAction(UserActionEventArgs e)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        string time = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        // Build structured action log payload
        var actionMessage = new Dictionary<string, object?>
        {
            ["name"] = e.EventName,
            ["surfaceId"] = e.SurfaceId,
            ["sourceComponentId"] = e.ComponentId,
            ["timestamp"] = timestamp,
            ["context"] = e.Payload,
        };

        string entry;
        try
        {
            string json = JsonSerializer.Serialize(actionMessage, s_indentedJson);
            entry = $"[{time}] Action dispatched: {e.SurfaceId}\n{json}";
        }
        catch (JsonException)
        {
            entry = $"[{time}] Action: {e.EventName} on {e.SurfaceId}";
        }

        this.ActionLogs.Insert(0, entry);
    }

    // ── SurfaceManager event handlers ─────────────────────

    private void OnSurfaceCreated(object? sender, SurfaceCreatedEventArgs e)
    {
        this.ActiveSurface = e.Surface;
        this.UpdateDataModelJson(e.Surface);
    }

    private void OnSurfaceDeleted(object? sender, SurfaceDeletedEventArgs e)
    {
        if (this.ActiveSurface?.SurfaceId == e.Surface.SurfaceId)
        {
            this.HasComponents = false;
            this.ActiveSurface = null;
        }
    }

    private void OnComponentsUpdated(object? sender, ComponentsUpdatedEventArgs e)
    {
        this.HasComponents = true;
        this.UpdateDataModelJson(e.Surface);
        this.SurfaceRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataModelUpdated(object? sender, DataModelUpdatedEventArgs e)
    {
        this.UpdateDataModelJson(e.Surface);
        this.SurfaceRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDataModelJson(Surface surface) =>
        this.CurrentDataModelJson = surface.DataModel.ToJson(indented: true);

    // ── Dispose ───────────────────────────────────────────

    public void Dispose()
    {
        this._manager.SurfaceCreated -= this.OnSurfaceCreated;
        this._manager.SurfaceDeleted -= this.OnSurfaceDeleted;
        this._manager.ComponentsUpdated -= this.OnComponentsUpdated;
        this._manager.DataModelUpdated -= this.OnDataModelUpdated;
    }
}
