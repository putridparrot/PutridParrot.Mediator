using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator;

/// <summary>
/// The services and options a <see cref="Mediator"/> uses while handling a call.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MediatorContext
{
    /// <summary>
    /// Initializes a new instance of the MediatorContext class.
    /// </summary>
    public MediatorContext(IServiceProvider serviceProvider, MediatorOptions options, ILogger? logger)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Logger = logger;
    }

    /// <summary>
    /// The provider handlers and behaviors are resolved from.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// The mediator options.
    /// </summary>
    public MediatorOptions Options { get; }

    /// <summary>
    /// Logger for notification handler failures, if available.
    /// </summary>
    public ILogger? Logger { get; }
}

/// <summary>
/// Strongly-typed mediator operations. Called by source-generated routes with concrete types,
/// so no reflection is involved, and by the mediator's runtime fallback.
/// </summary>
/// <remarks>
/// Not intended to be called directly; use <see cref="IMediator"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediatorRuntime
{
    /// <summary>
    /// Resolves the handler for <typeparamref name="TRequest"/> and invokes it through the pipeline behaviors.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    public static Task<TResponse> Send<TRequest, TResponse>(MediatorContext context, TRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = context.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw NoHandler(typeof(TRequest));

        return ExecutePipeline<TRequest, TResponse>(
            context.ServiceProvider, request, () => handler.Handle(request, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Resolves the handler for the command <typeparamref name="TRequest"/> and invokes it through the
    /// pipeline behaviors, with <see cref="Unit"/> as the response type.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the command type.</exception>
    public static Task SendCommand<TRequest>(MediatorContext context, TRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        var handler = context.ServiceProvider.GetService<IRequestHandler<TRequest>>()
            ?? throw NoHandler(typeof(TRequest));

        async Task<Unit> InvokeHandler()
        {
            await handler.Handle(request, cancellationToken);
            return Unit.Value;
        }

        return ExecutePipeline<TRequest, Unit>(context.ServiceProvider, request, InvokeHandler, cancellationToken);
    }

    /// <summary>
    /// Resolves the stream handler for <typeparamref name="TRequest"/> and returns its stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">No stream handler is registered for the request type.</exception>
    public static IAsyncEnumerable<TResponse> CreateStream<TRequest, TResponse>(MediatorContext context, TRequest request, CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        var handler = context.ServiceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No stream handler registered for request type {typeof(TRequest).FullName}");

        return handler.Handle(request, cancellationToken);
    }

    /// <summary>
    /// Publishes the notification to every registered handler, one at a time in registration order,
    /// handling failures according to <see cref="MediatorOptions.NotificationErrorHandling"/>.
    /// </summary>
    public static async Task Publish<TNotification>(MediatorContext context, TNotification notification, CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var errorHandling = context.Options.NotificationErrorHandling;
        List<Exception>? exceptions = null;

        foreach (var handler in context.ServiceProvider.GetServices<INotificationHandler<TNotification>>())
        {
            try
            {
                await handler.Handle(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (errorHandling != NotificationErrorHandling.StopOnFirstException)
            {
                if (errorHandling == NotificationErrorHandling.LogAndContinue)
                {
                    context.Logger?.LogError(ex, "Notification handler {Handler} failed handling {Notification}",
                        handler.GetType().FullName, typeof(TNotification).FullName);
                }
                else
                {
                    (exceptions ??= []).Add(ex);
                }
            }
        }

        if (exceptions is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }
        else if (exceptions is { Count: > 1 })
        {
            throw new AggregateException(
                $"{exceptions.Count} notification handlers failed handling {typeof(TNotification).FullName}", exceptions);
        }
    }

    private static Task<TResponse> ExecutePipeline<TRequest, TResponse>(
        IServiceProvider serviceProvider,
        TRequest request,
        Func<Task<TResponse>> handlerDelegate,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        IPipelineBehavior<TRequest, TResponse>[] behaviors;
        try
        {
            behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
        }
        catch (InvalidOperationException ex) when (!RuntimeFeature.IsDynamicCodeSupported && typeof(TResponse).IsValueType)
        {
            // The DI container can't close open generic services over value types without dynamic code
            throw new InvalidOperationException(
                $"Pipeline behaviors for {typeof(TRequest).Name} can't be created under Native AOT: its response type " +
                $"{typeof(TResponse).Name} is a value type and open generic behaviors (AddOpenBehavior) are registered. " +
                "Return a reference type (for example a record) from the request, or register behaviors for it as " +
                "closed types with AddBehavior<T>() instead of open generics.", ex);
        }

        if (behaviors.Length == 0)
        {
            return handlerDelegate();
        }

        // Build the pipeline from the inside out so the first registered behavior runs first
        RequestHandlerDelegate<TResponse> pipeline = () => handlerDelegate();

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = () => behavior.Handle(request, next, cancellationToken);
        }

        return pipeline();
    }

    private static InvalidOperationException NoHandler(Type requestType) =>
        new($"No handler registered for request type {requestType.FullName}");
}
