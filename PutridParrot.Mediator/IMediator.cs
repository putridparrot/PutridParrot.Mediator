namespace PutridParrot.Mediator;

/// <summary>
/// Defines a mediator for dispatching requests to their corresponding handlers.
/// Implements the mediator pattern to decouple request senders from request handlers.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a request to a single handler that returns a response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the request handler.</typeparam>
    /// <param name="request">The request object to be handled.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation, containing the response from the handler.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a request to a single handler that does not return a response.
    /// </summary>
    /// <param name="request">The request object to be handled.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    Task Send(IRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// All handlers are invoked sequentially, in registration order. By default, if a handler throws,
    /// the remaining handlers are still executed and the exception is rethrown afterwards
    /// (an <see cref="AggregateException"/> if several handlers failed).
    /// This is configurable with <see cref="MediatorConfiguration.NotificationErrorHandling"/>.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is null.</exception>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates an async stream for a streaming request that returns results incrementally.
    /// </summary>
    /// <typeparam name="TResponse">The type of each item in the response stream.</typeparam>
    /// <param name="request">The streaming request to handle.</param>
    /// <param name="cancellationToken">A cancellation token to observe while consuming the stream.</param>
    /// <returns>An async enumerable that yields responses as they become available.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the request type.</exception>
    /// <remarks>
    /// Use streaming requests for scenarios where:
    /// - Results should be processed as they become available
    /// - The full dataset is too large to load into memory
    /// - You want to provide incremental feedback to users
    /// - You're implementing server-sent events or similar patterns
    /// </remarks>
    /// <example>
    /// <code>
    /// await foreach (var order in mediator.CreateStream(new GetOrdersQuery(customerId)))
    /// {
    ///     Console.WriteLine($"Order: {order.Id}");
    /// }
    /// </code>
    /// </example>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request, 
        CancellationToken cancellationToken = default);
}

