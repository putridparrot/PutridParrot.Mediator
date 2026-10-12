using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator;

/// <summary>
/// Mediator implementation that routes requests, streams and notifications through source-generated,
/// reflection-free routes.
/// </summary>
/// <remarks>
/// Each project that references the package gets a generated <see cref="IMediatorRoutes"/> implementation.
/// Types no generated route knows about (for example handlers in a project without the generator) are handled
/// by delegates built once per type at runtime; that fallback is not available under Native AOT.
/// For logging, use <see cref="LoggingPipelineBehavior{TRequest,TResponse}"/> or call
/// <see cref="LoggingExtensions.AddMediatorLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// </remarks>
public class Mediator : IMediator
{
    private readonly MediatorContext _context;
    private readonly IMediatorRoutes[] _routes;

    /// <summary>
    /// Initializes a new instance of the Mediator class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve handlers.</param>
    /// <param name="routes">The generated routes; one per project that uses the source generator.</param>
    /// <param name="options">Optional runtime options; defaults are used when not supplied.</param>
    /// <param name="logger">Optional logger, used for notification handler failures with <see cref="NotificationErrorHandling.LogAndContinue"/>.</param>
    public Mediator(
        IServiceProvider serviceProvider,
        IEnumerable<IMediatorRoutes>? routes = null,
        MediatorOptions? options = null,
        ILogger<Mediator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _context = new MediatorContext(serviceProvider, options ?? new MediatorOptions(), logger);
        _routes = routes?.ToArray() ?? [];
    }

    /// <summary>
    /// Sends a request to its handler, executing any registered pipeline behaviors.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var routes in _routes)
        {
            var task = routes.Send(request, _context, cancellationToken);
            if (task != null)
            {
                return await task;
            }
        }

        return await RuntimeFallback.Send(request, _context, cancellationToken);
    }

    /// <summary>
    /// Sends a request that has no response to its handler, executing any registered pipeline behaviors
    /// (with <see cref="Unit"/> as the response type).
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    public async Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var routes in _routes)
        {
            var task = routes.Send(request, _context, cancellationToken);
            if (task != null)
            {
                await task;
                return;
            }
        }

        await RuntimeFallback.SendCommand(request, _context, cancellationToken);
    }

    /// <summary>
    /// Publishes a notification to all registered handlers, one at a time in registration order.
    /// Handler exceptions are handled according to <see cref="MediatorOptions.NotificationErrorHandling"/>.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        foreach (var routes in _routes)
        {
            var task = routes.Publish(notification, _context, cancellationToken);
            if (task != null)
            {
                await task;
                return;
            }
        }

        await RuntimeFallback.Publish(notification, _context, cancellationToken);
    }

    /// <summary>
    /// Creates a stream of responses from the handler registered for the streaming request.
    /// </summary>
    /// <param name="request">The streaming request.</param>
    /// <param name="cancellationToken">Cancellation token used while enumerating the stream.</param>
    /// <exception cref="InvalidOperationException">No stream handler is registered for the request type.</exception>
    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IAsyncEnumerable<TResponse>? stream = null;
        foreach (var routes in _routes)
        {
            stream = routes.CreateStream(request, _context, cancellationToken);
            if (stream != null)
            {
                break;
            }
        }

        stream ??= RuntimeFallback.CreateStream(request, _context, cancellationToken);

        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
