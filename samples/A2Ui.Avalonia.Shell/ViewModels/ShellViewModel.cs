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

            this.ProcessMessages(messages);

            ShellViewModelLog.ProcessedQueryMessages(this._logger, messages.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelled — nothing to do
        }
        catch (HttpRequestException ex)
        {
            ShellViewModelLog.SendQueryFailed(this._logger, ex);
            this.ErrorText = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            ShellViewModelLog.UnexpectedSendQueryError(this._logger, ex);
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
            ShellViewModelLog.DispatchingAction(this._logger, e.EventName, e.SurfaceId);

            IReadOnlyList<string> messages = await this._client.SendActionRawAsync(envelope, ct).ConfigureAwait(true);

            ShellViewModelLog.ProcessingMessages(this._logger, messages.Count, "action");

            this.ProcessMessages(messages);

            ShellViewModelLog.ProcessedActionMessages(this._logger, messages.Count, e.EventName);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (HttpRequestException ex)
        {
            ShellViewModelLog.SendActionFailed(this._logger, ex);
            this.ErrorText = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            ShellViewModelLog.UnexpectedSendActionError(this._logger, ex);
            this.ErrorText = $"Error: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private void ProcessMessages(IReadOnlyList<string> messages)
    {
        var errors = new List<string>();
        foreach (string msg in messages)
        {
            try
            {
                this._manager.Process(msg);
            }
            catch (Exception ex)
            {
                ShellViewModelLog.BatchMessageProcessingFailed(this._logger, ex);
                errors.Add(ex.Message);
            }
        }

        if (errors.Count > 0)
        {
            this.ErrorText = string.Join("; ", errors);
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
            ShellViewModelLog.AgentCardFetchFailed(this._logger, ex);
            this.StatusText = "Agent (offline)";
        }
    }

    // ── View event ────────────────────────────────────────

    public event EventHandler? SurfaceRefreshRequested;

    // ── SurfaceManager event handlers ─────────────────────

    private void OnSurfaceCreated(object? sender, SurfaceCreatedEventArgs e)
    {
        for (int i = 0; i < this.Surfaces.Count; i++)
        {
            if (this.Surfaces[i].SurfaceId == e.Surface.SurfaceId)
            {
                this.Surfaces[i] = e.Surface;
                SurfaceRefreshRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        this.Surfaces.Add(e.Surface);
    }

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

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Processed {Count} message(s) for query"
    )]
    public static partial void ProcessedQueryMessages(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Failed to send query to agent"
    )]
    public static partial void SendQueryFailed(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Unexpected error sending query"
    )]
    public static partial void UnexpectedSendQueryError(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Dispatching action: {Action} on {Surface}"
    )]
    public static partial void DispatchingAction(ILogger logger, string action, string surface);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Processed {Count} message(s) for action: {Action}"
    )]
    public static partial void ProcessedActionMessages(ILogger logger, int count, string action);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Failed to send action to agent"
    )]
    public static partial void SendActionFailed(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Error,
        Message = "Unexpected error sending action"
    )]
    public static partial void UnexpectedSendActionError(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Failed to process A2UI message in batch"
    )]
    public static partial void BatchMessageProcessingFailed(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Could not fetch agent card"
    )]
    public static partial void AgentCardFetchFailed(ILogger logger, Exception ex);
}
