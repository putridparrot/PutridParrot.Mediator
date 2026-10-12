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
    /// Whether this dispatcher has a strongly-typed case for the streaming request type.
    /// When false, the mediator invokes the handler through its own cached delegates instead.
    /// </summary>
    /// <param name="requestType">The runtime type of the streaming request.</param>
    /// <returns>True by default; generated dispatchers return true only for the request types they know.</returns>
    bool CanDispatch(Type requestType) => true;

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
