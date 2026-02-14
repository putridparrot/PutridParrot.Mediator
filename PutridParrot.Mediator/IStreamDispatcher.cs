namespace PutridParrot.Mediator;

/// <summary>
/// Interface for dispatching streaming requests to their handlers.
/// Implementations can use compile-time code generation for optimal performance.
/// </summary>
/// <remarks>
/// This interface is typically implemented by source-generated code that eliminates
/// reflection overhead when invoking streaming request handlers.
/// </remarks>
public interface IStreamDispatcher
{
    /// <summary>
    /// Dispatches a streaming request to its handler and returns the async enumerable stream.
    /// </summary>
    /// <typeparam name="TResponse">The type of items in the response stream.</typeparam>
    /// <param name="request">The streaming request to dispatch.</param>
    /// <param name="handler">The handler instance that will process the request.</param>
    /// <param name="cancellationToken">Cancellation token for the stream.</param>
    /// <returns>An async enumerable stream of responses.</returns>
    IAsyncEnumerable<TResponse> DispatchStream<TResponse>(
        IStreamRequest<TResponse> request,
        object handler,
        CancellationToken cancellationToken);
}
