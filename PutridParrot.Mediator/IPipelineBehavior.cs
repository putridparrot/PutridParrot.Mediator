namespace PutridParrot.Mediator;

/// <summary>
/// Pipeline behavior for cross-cutting concerns
/// Executes before and after handler execution
/// </summary>
/// <typeparam name="TRequest">The type of request</typeparam>
/// <typeparam name="TResponse">The type of response</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> 
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handle the request and optionally call the next behavior in the pipeline
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="next">Delegate to call the next behavior or handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the handler</returns>
    Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken);
}

/// <summary>
/// Delegate that represents the next handler in the pipeline
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
