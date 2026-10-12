namespace PutridParrot.Mediator;

/// <summary>
/// Sends requests and streaming requests to their single handler.
/// Depend on this instead of <see cref="IMediator"/> when a class only sends requests.
/// </summary>
public interface ISender
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
