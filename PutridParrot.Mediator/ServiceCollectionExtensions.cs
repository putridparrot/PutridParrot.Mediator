using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace PutridParrot.Mediator;

/// <summary>
/// Extension methods for registering mediator services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fast mediator (with source-generated dispatcher) and optionally scans assemblies for request handlers.
    /// This provides better performance than the standard mediator by eliminating reflection overhead.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="assemblies">Optional assemblies to scan for request handlers. If none provided, only the mediator is registered.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers <see cref="Mediator"/> which uses source-generated dispatch code
    /// for optimal performance. It also registers the <see cref="IRequestDispatcher"/> if available.
    /// All discovered handlers are registered as transient services.
    /// </remarks>
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
        var assemblies = configuration.Assemblies.ToArray();

        // Find all generated dispatchers across assemblies
        var dispatcherTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "GeneratedRequestDispatcher" && 
                       t.GetInterfaces().Any(i => i.Name == "IRequestDispatcher"))
            .ToList();
        
        if (dispatcherTypes.Count > 0)
        {
            if (dispatcherTypes.Count == 1)
            {
                // Single dispatcher - register directly
                services.AddSingleton(typeof(IRequestDispatcher), dispatcherTypes[0]);
            }
            else
            {
                // Multiple dispatchers - use composite pattern
                services.AddSingleton<IRequestDispatcher>(sp =>
                {
                    var dispatchers = dispatcherTypes
                        .Select(t => (IRequestDispatcher)Activator.CreateInstance(t)!)
                        .ToList();
                    return new CompositeRequestDispatcher(dispatchers);
                });
            }
        }
        
        // Find and register generated stream dispatchers
        var streamDispatcherTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "GeneratedStreamDispatcher" && 
                       t.GetInterfaces().Any(i => i.Name == "IStreamDispatcher"))
            .ToList();
        
        if (streamDispatcherTypes.Count > 0)
        {
            // For now, just register the first one found
            // TODO: Could implement composite pattern for multiple stream dispatchers if needed
            services.AddSingleton(typeof(IStreamDispatcher), streamDispatcherTypes[0]);
        }
        
        services.AddSingleton<IMediator, Mediator>();
        
        if (assemblies.Length > 0)
        {
            services.AddRequestHandlers(assemblies);
        }

        // Registration order is execution order, so add behaviors exactly as configured
        foreach (var behavior in configuration.Behaviors)
        {
            services.Add(behavior);
        }

        return services;
    }

    
    /// <summary>
    /// Scans the specified assemblies and registers all request handlers with the service collection.
    /// </summary>
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var handlerType in handlerTypes)
        {
            // Register each IRequestHandler<,>, IRequestHandler<> and IStreamRequestHandler<,> the type implements
            foreach (var @interface in handlerType.GetInterfaces().Where(IsHandlerInterface))
            {
                services.AddTransient(@interface, handlerType);
            }
        }

        return services;
    }

    private static bool IsHandlerInterface(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IRequestHandler<,>) ||
               definition == typeof(IRequestHandler<>) ||
               definition == typeof(IStreamRequestHandler<,>);
    }
}


