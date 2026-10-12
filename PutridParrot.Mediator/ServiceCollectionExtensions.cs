using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace PutridParrot.Mediator;

/// <summary>
/// Extension methods for registering mediator services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mediator and optionally scans assemblies for handlers, validators and exception handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="assemblies">Optional assemblies to scan for handlers. If none provided, only the mediator is registered.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers <see cref="Mediator"/> as a transient service, along with any source-generated
    /// dispatchers found in the assemblies for optimal performance.
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

        // Register the generated dispatchers; one per assembly that uses the source generator
        var requestDispatchers = FindGeneratedTypes<IRequestDispatcher>(assemblies, "GeneratedRequestDispatcher");
        if (requestDispatchers.Count == 1)
        {
            services.AddSingleton(typeof(IRequestDispatcher), requestDispatchers[0]);
        }
        else if (requestDispatchers.Count > 1)
        {
            services.AddSingleton<IRequestDispatcher>(_ => new CompositeRequestDispatcher(
                requestDispatchers.Select(t => (IRequestDispatcher)Activator.CreateInstance(t)!)));
        }

        var streamDispatchers = FindGeneratedTypes<IStreamDispatcher>(assemblies, "GeneratedStreamDispatcher");
        if (streamDispatchers.Count == 1)
        {
            services.AddSingleton(typeof(IStreamDispatcher), streamDispatchers[0]);
        }
        else if (streamDispatchers.Count > 1)
        {
            services.AddSingleton<IStreamDispatcher>(_ => new CompositeStreamDispatcher(
                streamDispatchers.Select(t => (IStreamDispatcher)Activator.CreateInstance(t)!)));
        }

        services.AddSingleton(new MediatorOptions
        {
            NotificationErrorHandling = configuration.NotificationErrorHandling
        });

        // Transient so the mediator resolves handlers from the current scope (e.g. per HTTP request),
        // allowing handlers to depend on scoped services such as a DbContext
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

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
    /// Scans the specified assemblies and registers all request, stream and notification handlers,
    /// request validators and request exception handlers as transient services.
    /// </summary>
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var handlerType in handlerTypes)
        {
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
               definition == typeof(IStreamRequestHandler<,>) ||
               definition == typeof(INotificationHandler<>) ||
               definition == typeof(IRequestValidator<>) ||
               definition == typeof(IRequestExceptionHandler<,>);
    }

    private static List<Type> FindGeneratedTypes<TInterface>(Assembly[] assemblies, string typeName)
    {
        return assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == typeName && typeof(TInterface).IsAssignableFrom(t))
            .ToList();
    }
}


