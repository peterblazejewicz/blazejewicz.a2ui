using System.Collections.ObjectModel;
using Blazejewicz.A2Ui.Avalonia.Shell.Services;
using Blazejewicz.A2Ui.Core.Surfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Blazejewicz.A2Ui.Avalonia.Shell.ViewModels;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IA2AClient _client;
    private readonly SurfaceManager _manager;
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(IA2AClient client, SurfaceManager manager, ILogger<ShellViewModel> logger)
    {
        this._client = client;
        this._manager = manager;
        this._logger = logger;

        this._manager.SurfaceCreated += this.OnSurfaceCreated;
        this._manager.SurfaceDeleted += this.OnSurfaceDeleted;
        this._manager.ComponentsUpdated += this.OnComponentsUpdated;
        this._manager.DataModelUpdated += this.OnDataModelUpdated;
    }

    // ── State ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _promptText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Connecting...";

    [ObservableProperty]
    private string? _errorText;

    public ObservableCollection<Surface> Surfaces { get; } = [];

    // ── Commands ──────────────────────────────────────────

    private bool CanSend => !this.IsBusy && !string.IsNullOrWhiteSpace(this.PromptText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        string query = this.PromptText.Trim();
        this.PromptText = string.Empty;
        this.ErrorText = null;

        this.IsBusy = true;
        try
        {
            this._manager.Clear();

            IReadOnlyList<string> messages = await this._client.SendTextRawAsync(query, ct).ConfigureAwait(true);

            ShellViewModelLog.ProcessingMessages(this._logger, messages.Count, "query");

            foreach (string msg in messages)
            {
                this._manager.Process(msg);
            }

            this._logger.LogInformation("Processed {Count} message(s) for query: {Query}", messages.Count, query);
        }
        catch (OperationCanceledException)
        {
            // Cancelled — nothing to do
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Failed to send query to agent");
            this.ErrorText = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Unexpected error sending query");
            this.ErrorText = $"Error: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    // ── Action dispatch ───────────────────────────────────

    public async Task HandleUserActionAsync(UserActionEventArgs e, CancellationToken ct = default)
    {
        if (this.IsBusy)
        {
            return;
        }

        this.ErrorText = null;
        this.IsBusy = true;
        try
        {
            string? protocolVersion = this._manager.GetSurface(e.SurfaceId)?.ProtocolVersion;
            object envelope = UserActionSerializer.Serialize(e, protocolVersion);
            this._logger.LogInformation("Dispatching action: {Action} on {Surface}", e.EventName, e.SurfaceId);

            this._manager.Clear();

            IReadOnlyList<string> messages = await this._client.SendActionRawAsync(envelope, ct).ConfigureAwait(true);

            ShellViewModelLog.ProcessingMessages(this._logger, messages.Count, "action");

            foreach (string msg in messages)
            {
                this._manager.Process(msg);
            }

            this._logger.LogInformation(
                "Processed {Count} message(s) for action: {Action}",
                messages.Count,
                e.EventName
            );
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Failed to send action to agent");
            this.ErrorText = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Unexpected error sending action");
            this.ErrorText = $"Error: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    // ── Initialization ────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            this.StatusText = await this._client.GetAgentNameAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Could not fetch agent card");
            this.StatusText = "Agent (offline)";
        }
    }

    // ── View event ────────────────────────────────────────

    public event EventHandler? SurfaceRefreshRequested;

    // ── SurfaceManager event handlers ─────────────────────

    private void OnSurfaceCreated(object? sender, SurfaceCreatedEventArgs e) => this.Surfaces.Add(e.Surface);

    private void OnSurfaceDeleted(object? sender, SurfaceDeletedEventArgs e)
    {
        for (int i = this.Surfaces.Count - 1; i >= 0; i--)
        {
            if (this.Surfaces[i].SurfaceId == e.Surface.SurfaceId)
            {
                this.Surfaces.RemoveAt(i);
                break;
            }
        }
    }

    private void OnComponentsUpdated(object? sender, ComponentsUpdatedEventArgs e) =>
        SurfaceRefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnDataModelUpdated(object? sender, DataModelUpdatedEventArgs e) =>
        SurfaceRefreshRequested?.Invoke(this, EventArgs.Empty);

    // ── Dispose ───────────────────────────────────────────

    public void Dispose()
    {
        this._manager.SurfaceCreated -= this.OnSurfaceCreated;
        this._manager.SurfaceDeleted -= this.OnSurfaceDeleted;
        this._manager.ComponentsUpdated -= this.OnComponentsUpdated;
        this._manager.DataModelUpdated -= this.OnDataModelUpdated;
    }
}

internal static partial class ShellViewModelLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Processing {MessageCount} A2UI message(s) from {Trigger}"
    )]
    public static partial void ProcessingMessages(ILogger logger, int messageCount, string trigger);
}
