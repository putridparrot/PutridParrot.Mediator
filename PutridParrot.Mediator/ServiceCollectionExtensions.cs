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
    /// <example>
    /// <code>
    /// services.AddMediator(typeof(MyHandler).Assembly);
    /// </code>
    /// </example>
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
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
        
        services.AddSingleton<IMediator, Mediator>();
        
        if (assemblies.Length > 0)
        {
            services.AddRequestHandlers(assemblies);
        }
        
        return services;
    }
    
    /// <summary>
    /// Scans the specified assemblies and registers all request handlers with the service collection.
    /// </summary>
    /// <param name="services">The service collection to add handlers to.</param>
    /// <param name="assemblies">The assemblies to scan for request handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method discovers all non-abstract classes that implement <see cref="IRequestHandler{TRequest}"/>
    /// or <see cref="IRequestHandler{TRequest, TResponse}"/> or are marked with <see cref="RequestHandlerAttribute"/>.
    /// All discovered handlers are registered as transient services.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddRequestHandlers(Assembly.GetExecutingAssembly());
    /// </code>
    /// </example>
    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var handlerTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && 
                       (IsRequestHandler(t) || HasRequestHandlerAttribute(t)));
        
        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && 
                           (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                            i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)));
            
            foreach (var @interface in interfaces)
            {
                services.AddTransient(@interface, handlerType);
            }
        }
        
        return services;
    }
    
    private static bool IsRequestHandler(Type type)
    {
        return type.GetInterfaces().Any(i => 
            i.IsGenericType && 
            (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
             i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)));
    }
    
    private static bool HasRequestHandlerAttribute(Type type)
    {
        return type.GetCustomAttribute<RequestHandlerAttribute>() != null;
    }
}
