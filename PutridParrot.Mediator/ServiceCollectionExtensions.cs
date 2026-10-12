using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PutridParrot.Mediator;

/// <summary>
/// Extension methods for registering mediator services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    internal const string ScanningMessage =
        "Assembly scanning uses reflection to find handlers, which may be removed by trimming. " +
        "For trimmed or Native AOT apps call AddGeneratedMediatorHandlers() (generated in each project) instead.";

    /// <summary>
    /// Registers the mediator without scanning for handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Combine with the generated <c>AddGeneratedMediatorHandlers()</c> of each project for a reflection-free,
    /// trimming and Native AOT compatible setup.
    /// </remarks>
    public static IServiceCollection AddMediator(this IServiceCollection services)
    {
        return services.AddMediator(_ => { });
    }

    /// <summary>
    /// Registers the mediator and scans assemblies for handlers, validators and exception handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers <see cref="Mediator"/> as a transient service, along with the source-generated
    /// routes found in the assemblies. All discovered handlers are registered as transient services.
    /// </remarks>
    [RequiresUnreferencedCode(ScanningMessage)]
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        return services.AddMediator(cfg => cfg.RegisterServicesFromAssemblies(assemblies));
    }

    /// <summary>
    /// Registers the mediator, scans the configured assemblies for handlers and registers
    /// the configured pipeline behaviors.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Configures assemblies to scan and the pipeline behaviors to apply.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Behaviors run in the order they are added to the <see cref="MediatorConfiguration"/>;
    /// the first behavior added is the outermost.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddMediator(cfg =>
    /// {
    ///     cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    ///     cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior&lt;,&gt;));
    ///     cfg.AddOpenBehavior(typeof(CachingBehavior&lt;,&gt;));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new MediatorConfiguration();
        configure(configuration);

        services.AddSingleton(new MediatorOptions
        {
            NotificationErrorHandling = configuration.NotificationErrorHandling
        });

        // Transient so the mediator resolves handlers from the current scope (e.g. per HTTP request),
        // allowing handlers to depend on scoped services such as a DbContext
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        if (configuration.Assemblies.Count > 0)
        {
            ScanConfiguredAssemblies(services, configuration.Assemblies);
        }

        // Registration order is execution order, so add behaviors exactly as configured
        foreach (var behavior in configuration.Behaviors)
        {
            services.Add(behavior);
        }

        return services;
    }

    /// <summary>
    /// Scans the specified assemblies and registers all request, stream and notification handlers,
    /// request validators and request exception handlers as transient services, along with the
    /// assemblies' source-generated routes.
    /// </summary>
    [RequiresUnreferencedCode(ScanningMessage)]
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var types = assemblies.SelectMany(a => a.GetTypes()).ToList();

        // The generated routes of each assembly; TryAddEnumerable avoids duplicates when the generated
        // registration method is also called
        foreach (var routesType in types.Where(t => t.Name == "GeneratedMediatorRoutes" && typeof(IMediatorRoutes).IsAssignableFrom(t)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IMediatorRoutes), routesType));
        }

        foreach (var handlerType in types.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var @interface in handlerType.GetInterfaces().Where(IsHandlerInterface))
            {
                // TryAddEnumerable makes registration idempotent, e.g. when also calling the generated registration
                services.TryAddEnumerable(ServiceDescriptor.Transient(@interface, handlerType));
            }
        }

        return services;
    }

    // Only reached when MediatorConfiguration.RegisterServicesFromAssembly was called, which carries the warning
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers of RegisterServicesFromAssembly receive the RequiresUnreferencedCode warning.")]
    private static void ScanConfiguredAssemblies(IServiceCollection services, IReadOnlyList<Assembly> assemblies)
    {
        services.AddRequestHandlers(assemblies.ToArray());
    }

    private static bool IsHandlerInterface(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IRequestHandler<,>) ||
               definition == typeof(IRequestHandler<>) ||
               definition == typeof(IStreamRequestHandler<,>) ||
               definition == typeof(INotificationHandler<>) ||
               definition == typeof(IRequestValidator<>) ||
               definition == typeof(IRequestExceptionHandler<,>);
    }
}
