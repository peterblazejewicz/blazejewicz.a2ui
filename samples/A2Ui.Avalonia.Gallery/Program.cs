using System.Text;
using Avalonia;
using Blazejewicz.A2Ui.Avalonia.Gallery.Services;
using Blazejewicz.A2Ui.Avalonia.Gallery.ViewModels;
using Blazejewicz.A2Ui.Avalonia.Gallery.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Blazejewicz.A2Ui.Avalonia.Gallery;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Ignore when stdout is not attached to a console handle.
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "gallery-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7
            )
            .Enrich.FromLogContext()
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        ConfigureServices(services);
        App.Services = services.BuildServiceProvider();

        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddA2UiCore();
        services.AddA2UiAvalonia();
        services.AddSingleton<GalleryDataLoader>();
        services.AddSingleton<GalleryViewModel>();
        services.AddTransient<GalleryWindow>();
    }
}
