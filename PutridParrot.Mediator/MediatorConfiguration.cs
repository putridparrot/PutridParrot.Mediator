using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator;

/// <summary>
/// Configuration used by <see cref="ServiceCollectionExtensions.AddMediator(IServiceCollection, Action{MediatorConfiguration})"/>
/// to declare which assemblies to scan for handlers and which pipeline behaviors to register.
/// </summary>
/// <remarks>
/// Behaviors execute in the order they are added: the first behavior added is the outermost,
/// so it runs first before the handler and last after it.
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
public sealed class MediatorConfiguration
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<ServiceDescriptor> _behaviors = [];

    /// <summary>
    /// The assemblies that will be scanned for request and stream handlers.
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>
    /// The pipeline behavior registrations, in execution order.
    /// </summary>
    public IReadOnlyList<ServiceDescriptor> Behaviors => _behaviors;

    /// <summary>
    /// The lifetime used for behaviors added after this is set. Default is <see cref="ServiceLifetime.Transient"/>.
    /// </summary>
    public ServiceLifetime BehaviorLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Scans the assembly for request and stream handlers (and generated dispatchers).
    /// </summary>
    public MediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }
        return this;
    }

    /// <summary>
    /// Scans the assemblies for request and stream handlers (and generated dispatchers).
    /// </summary>
    public MediatorConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            RegisterServicesFromAssembly(assembly);
        }
        return this;
    }

    /// <summary>
    /// Adds an open generic pipeline behavior that applies to every request,
    /// for example <c>typeof(CachingBehavior&lt;,&gt;)</c>.
    /// </summary>
    /// <param name="openBehaviorType">An open generic type implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <exception cref="ArgumentException">The type is not an open generic pipeline behavior.</exception>
    public MediatorConfiguration AddOpenBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        if (!openBehaviorType.IsGenericTypeDefinition ||
            !openBehaviorType.GetInterfaces().Any(IsPipelineBehaviorInterface))
        {
            throw new ArgumentException(
                $"{openBehaviorType.FullName} must be an open generic type implementing IPipelineBehavior<,>, e.g. typeof(MyBehavior<,>).",
                nameof(openBehaviorType));
        }

        _behaviors.Add(new ServiceDescriptor(typeof(IPipelineBehavior<,>), openBehaviorType, BehaviorLifetime));
        return this;
    }

    /// <summary>
    /// Adds a closed pipeline behavior, registered for each <see cref="IPipelineBehavior{TRequest,TResponse}"/>
    /// it implements, so it only applies to those request types.
    /// </summary>
    /// <typeparam name="TBehavior">A non-generic behavior class.</typeparam>
    /// <exception cref="ArgumentException">The type does not implement a closed pipeline behavior.</exception>
    public MediatorConfiguration AddBehavior<TBehavior>()
        where TBehavior : class
    {
        var behaviorType = typeof(TBehavior);
        var interfaces = behaviorType.GetInterfaces().Where(IsPipelineBehaviorInterface).ToList();

        if (behaviorType.IsGenericTypeDefinition || interfaces.Count == 0)
        {
            throw new ArgumentException(
                $"{behaviorType.FullName} must implement IPipelineBehavior<TRequest, TResponse> for a specific request type. Use AddOpenBehavior for open generic behaviors.",
                nameof(TBehavior));
        }

        foreach (var @interface in interfaces)
        {
            _behaviors.Add(new ServiceDescriptor(@interface, behaviorType, BehaviorLifetime));
        }
        return this;
    }

    private static bool IsPipelineBehaviorInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
}
