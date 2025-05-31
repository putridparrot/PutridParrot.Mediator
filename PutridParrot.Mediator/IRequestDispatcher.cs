namespace PutridParrot.Mediator;

/// <summary>
/// Interface for generated request dispatcher that uses direct method calls instead of reflection.
/// Implemented by source generator for optimal performance.
/// </summary>
public interface IRequestDispatcher
{
    /// <summary>
    /// Dispatches a request with response to its handler using a direct, strongly-typed call.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="handler">The handler to invoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the handler.</returns>
    Task<TResponse> Dispatch<TResponse>(
        IRequest<TResponse> request, 
        object handler, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches a request without response to its handler using a direct, strongly-typed call.
    /// </summary>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="handler">The handler to invoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Dispatch(
        IRequest request, 
        object handler, 
        CancellationToken cancellationToken);
}
