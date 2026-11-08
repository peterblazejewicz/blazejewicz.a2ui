using System.Text;
using Avalonia;
using Blazejewicz.A2Ui.Avalonia.Shell.Models;
using Blazejewicz.A2Ui.Avalonia.Shell.Services;
using Blazejewicz.A2Ui.Avalonia.Shell.ViewModels;
using Blazejewicz.A2Ui.Avalonia.Shell.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Blazejewicz.A2Ui.Avalonia.Shell;

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
                Path.Combine(AppContext.BaseDirectory, "logs", "shell-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7
            )
            .Enrich.FromLogContext()
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        ConfigureServices(services);
        App.Services = services.BuildServiceProvider();

        // Subscribe the per-request summary logger before any HTTP traffic runs.
        _ = App.Services.GetRequiredService<RequestSummaryLogger>();

        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Agent configuration — default to Python restaurant_finder on localhost
        services.AddSingleton(
            new AgentConfig(
                ServerUrl: "http://localhost:10002",
                Title: "Restaurant Finder",
                Placeholder: "Ask about restaurants..."
            )
        );

        services.AddA2UiCore();
        services.AddA2UiAvalonia();

        // Register transient logging handler for outbound IA2AClient HTTP requests.
        services.AddTransient<LoggingHttpMessageHandler>();

        // Typed HttpClient for the A2A agent (negotiates v1.0, v0.9.1, and v0.9
        // A2UI extension URIs via X-A2A-Extensions).
        services
            .AddHttpClient<IA2AClient, A2AAgentClient>(http =>
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation(
                    A2AAgentClient.A2UiExtensionHeader,
                    A2AAgentClient.A2UiExtensionUri
                );
            })
            .AddHttpMessageHandler<LoggingHttpMessageHandler>();

        // Register ActivityListener that logs a single summary line per A2A.SendMessage span.
        services.AddSingleton<RequestSummaryLogger>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<ShellWindow>();
    }
}
