namespace PutridParrot.Mediator;

/// <summary>
/// Defines a handler for a request that returns a response.
/// Implement this interface to create a handler for a specific request type.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
/// <remarks>
/// Handlers implementing this interface are automatically registered when using
/// <see cref="ServiceCollectionExtensions.AddRequestHandlers"/> method.
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the specified request and returns a response.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation, containing the response.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a handler for a request that does not return a response.
/// Implement this interface to create a handler for a specific request type that performs an action.
/// </summary>
/// <typeparam name="TRequest">The type of request to be handled. Must implement <see cref="IRequest"/>.</typeparam>
/// <remarks>
/// Handlers implementing this interface are automatically registered when using
/// <see cref="ServiceCollectionExtensions.AddRequestHandlers"/> method.
/// </remarks>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// <summary>
    /// Handles the specified request.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Handle(TRequest request, CancellationToken cancellationToken = default);
}

