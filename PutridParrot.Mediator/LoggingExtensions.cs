using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator;

/// <summary>
/// Extension methods for adding logging capabilities to the mediator.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Adds logging pipeline behavior to all requests.
    /// Logs request execution at Debug level and errors at Error level.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This registers an open generic pipeline behavior that will log all requests.
    /// Make sure to configure an ILogger implementation (e.g., via AddLogging()) before calling this.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddLogging(builder => builder.AddConsole());
    /// services.AddMediator(typeof(Program).Assembly);
    /// services.AddMediatorLogging(); // Enable logging for all requests
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorLogging(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingPipelineBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds logging pipeline behavior for a specific request type.
    /// Logs request execution at Debug level and errors at Error level.
    /// </summary>
    /// <typeparam name="TRequest">The type of request to log.</typeparam>
    /// <typeparam name="TResponse">The type of response.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Use this when you only want to log specific request types instead of all requests.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddLogging(builder => builder.AddConsole());
    /// services.AddMediator(typeof(Program).Assembly);
    /// services.AddMediatorLogging&lt;ImportantRequest, ImportantResponse&gt;(); // Only log this specific request
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorLogging<TRequest, TResponse>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
    {
        services.AddTransient<IPipelineBehavior<TRequest, TResponse>, LoggingPipelineBehavior<TRequest, TResponse>>();
        return services;
    }
}
