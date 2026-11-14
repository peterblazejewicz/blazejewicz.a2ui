using Blazejewicz.A2Ui.Avalonia;
using Blazejewicz.A2Ui.Avalonia.Catalog;
using Blazejewicz.A2Ui.Core.Functions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering <c>A2Ui.Avalonia</c> renderer services with a
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class A2UiAvaloniaServiceCollectionExtensions
{
    /// <summary>
    /// Register the default catalog (<see cref="CatalogRegistry.CreateDefault(ILoggerFactory?, A2UiAvaloniaOptions?)"/>)
    /// and function registry (<see cref="FunctionRegistry.CreateDefault(ILoggerFactory?)"/>) as
    /// singletons. Both resolve an optional <see cref="ILoggerFactory"/> from the container so
    /// catalog entries and function evaluation participate in the app's logging pipeline.
    /// </summary>
    /// <param name="services">The DI service collection to register into.</param>
    /// <param name="configure">Optional callback to configure <see cref="A2UiAvaloniaOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// This does not register <c>A2UiRenderer</c> or the <c>A2UiSurface</c> control because
    /// both are constructed per-surface-host (the Avalonia control creates its own renderer
    /// to scope the control cache and subscriptions to its surface).
    /// </remarks>
    public static IServiceCollection AddA2UiAvalonia(
        this IServiceCollection services,
        Action<A2UiAvaloniaOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<A2UiAvaloniaOptions>();
        }

        services.TryAddSingleton(sp =>
            CatalogRegistry.CreateDefault(
                sp.GetService<ILoggerFactory>(),
                sp.GetService<IOptions<A2UiAvaloniaOptions>>()?.Value
            )
        );
        services.TryAddSingleton<IFunctionRegistry>(sp =>
            FunctionRegistry.CreateDefault(sp.GetService<ILoggerFactory>())
        );

        return services;
    }
}
