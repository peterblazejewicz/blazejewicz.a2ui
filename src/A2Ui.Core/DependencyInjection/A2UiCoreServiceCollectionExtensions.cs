using Blazejewicz.A2Ui.Core.Functions;
using Blazejewicz.A2Ui.Core.Surfaces;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering <c>A2Ui.Core</c> services with a
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class A2UiCoreServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="SurfaceManager"/> (singleton), <see cref="IFunctionRegistry"/> (singleton),
    /// and bind <see cref="SurfaceManagerOptions"/> so consumers can inject either directly.
    /// </summary>
    /// <param name="services">The DI service collection to register into.</param>
    /// <param name="configure">
    /// Optional policy configurator. Typical use:
    /// <c>services.AddA2UiCore(opts => opts.StrictMode = true)</c>.
    /// When <see langword="null"/>, default options are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddA2UiCore(
        this IServiceCollection services,
        Action<SurfaceManagerOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<SurfaceManagerOptions>();
        }

        services.TryAddSingleton(sp => new SurfaceManager(
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<SurfaceManagerOptions>>().Value
        ));
        services.TryAddSingleton<IFunctionRegistry>(sp =>
            FunctionRegistry.CreateDefault(sp.GetService<ILoggerFactory>())
        );

        return services;
    }
}
