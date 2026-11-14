using Avalonia.Controls;
using Blazejewicz.A2Ui.Avalonia.Controls;
using Blazejewicz.A2Ui.Avalonia.Gallery.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazejewicz.A2Ui.Avalonia.Gallery.Views;

public partial class GalleryWindow : Window
{
    private readonly ILogger<GalleryWindow> _logger;
    private GalleryViewModel? _vm;
    private A2UiSurface? _surfaceHost;

    // Parameterless constructor required by Avalonia XAML loader (AVLN3001).
    public GalleryWindow()
        : this(NullLogger<GalleryWindow>.Instance) { }

    public GalleryWindow(ILogger<GalleryWindow> logger)
    {
        this._logger = logger;
        this.InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from previous ViewModel if DataContext changes
        this.UnwireViewModel();

        if (this.DataContext is GalleryViewModel vm)
        {
            this._vm = vm;
            this._surfaceHost = this.FindControl<A2UiSurface>("SurfaceHost");

            if (this._surfaceHost is null)
            {
                GalleryWindowLog.SurfaceHostNotFound(this._logger);
                return;
            }

            // Wire logging into the XAML-constructed A2UiSurface so that
            // renderer, function registry, and image loading all log via Serilog.
            var loggerFactory = App.Services.GetService<ILoggerFactory>();
            if (loggerFactory is not null)
            {
                this._surfaceHost.SetLoggerFactory(loggerFactory);
            }

            vm.SurfaceRefreshRequested += this.OnSurfaceRefreshRequested;
            this._surfaceHost.UserActionFired += this.OnUserActionFired;
            this._surfaceHost.DataModelChanged += this.OnDataModelChanged;
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (this.DataContext is GalleryViewModel vm)
        {
            try
            {
                await vm.InitializeAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Window closed during initialization — nothing to do
            }
            catch (Exception ex)
            {
                GalleryWindowLog.GalleryInitializationFailed(this._logger, ex);
                this.Title = $"A2UI Gallery — Error: {ex.Message}";
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        this.UnwireViewModel();
        (this._vm as IDisposable)?.Dispose();
        this._vm = null;
        base.OnClosed(e);
    }

    private void OnSurfaceRefreshRequested(object? sender, EventArgs e) => this._surfaceHost?.Refresh();

    private void OnUserActionFired(object? sender, UserActionEventArgs args) => this._vm?.LogAction(args);

    private void OnDataModelChanged(object? sender, DataModelChangedEventArgs e)
    {
        this._vm?.RefreshDataModelJson();
        // Re-render the surface so that check validations, data-bound text,
        // and button enabled state update after two-way binding changes.
        this._surfaceHost?.Refresh();
    }

    private void UnwireViewModel()
    {
        this._vm?.SurfaceRefreshRequested -= this.OnSurfaceRefreshRequested;

        if (this._surfaceHost is not null)
        {
            this._surfaceHost.UserActionFired -= this.OnUserActionFired;
            this._surfaceHost.DataModelChanged -= this.OnDataModelChanged;
        }
    }
}

internal static partial class GalleryWindowLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "SurfaceHost control not found in visual tree"
    )]
    public static partial void SurfaceHostNotFound(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to initialize gallery")]
    public static partial void GalleryInitializationFailed(ILogger logger, Exception ex);
}
