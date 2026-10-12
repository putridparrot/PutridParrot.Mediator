namespace PutridParrot.Mediator;

/// <summary>
/// Handles an exception thrown while processing a request, optionally recovering with a response.
/// Used by <see cref="ExceptionHandlingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type, or <see cref="Unit"/> for commands.</typeparam>
/// <remarks>
/// Implementations are registered automatically by <c>AddMediator(assemblies)</c>.
/// Handlers are asked in registration order; the first to return a handled result wins.
/// </remarks>
/// <example>
/// <code>
/// public class GetUserNotFoundHandler : IRequestExceptionHandler&lt;GetUserQuery, UserDto?&gt;
/// {
///     public Task&lt;RequestExceptionResult&lt;UserDto?&gt;&gt; Handle(GetUserQuery request, Exception exception, CancellationToken cancellationToken) =>
///         Task.FromResult(exception is KeyNotFoundException
///             ? RequestExceptionResult&lt;UserDto?&gt;.Handled(null)
///             : RequestExceptionResult&lt;UserDto?&gt;.NotHandled);
/// }
/// </code>
/// </example>
public interface IRequestExceptionHandler<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Inspects the exception and decides whether to recover.
    /// </summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="exception">The exception thrown by the handler or a later behavior.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="RequestExceptionResult{TResponse}.Handled"/> to return a response instead of throwing,
    /// or <see cref="RequestExceptionResult{TResponse}.NotHandled"/> to let the exception propagate.
    /// </returns>
    Task<RequestExceptionResult<TResponse>> Handle(TRequest request, Exception exception, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of an <see cref="IRequestExceptionHandler{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public readonly record struct RequestExceptionResult<TResponse>
{
    private RequestExceptionResult(bool isHandled, TResponse response)
    {
        IsHandled = isHandled;
        Response = response;
    }

    /// <summary>
    /// Whether the exception was handled.
    /// </summary>
    public bool IsHandled { get; }

    /// <summary>
    /// The response to return when <see cref="IsHandled"/> is true.
    /// </summary>
    public TResponse Response { get; }

    /// <summary>
    /// The exception was handled; <paramref name="response"/> is returned to the caller.
    /// </summary>
    public static RequestExceptionResult<TResponse> Handled(TResponse response) => new(true, response);

    /// <summary>
    /// The exception was not handled and continues to propagate.
    /// </summary>
    public static RequestExceptionResult<TResponse> NotHandled => default;
}

/// <summary>
/// Pipeline behavior that gives registered <see cref="IRequestExceptionHandler{TRequest,TResponse}"/>
/// instances the chance to recover from exceptions thrown by the rest of the pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of request.</typeparam>
/// <typeparam name="TResponse">The type of response, or <see cref="Unit"/> for commands.</typeparam>
/// <remarks>
/// Add it before other behaviors so it also sees their exceptions (for example validation failures).
/// Cancellation exceptions caused by the request's own cancellation token are never handled.
/// </remarks>
public class ExceptionHandlingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestExceptionHandler<TRequest, TResponse>> _handlers;

    /// <summary>
    /// Initializes a new instance of the ExceptionHandlingBehavior class.
    /// </summary>
    /// <param name="handlers">The exception handlers registered for the request type.</param>
    public ExceptionHandlingBehavior(IEnumerable<IRequestExceptionHandler<TRequest, TResponse>> handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <summary>
    /// Calls the next behavior or handler, offering any exception to the registered exception handlers.
    /// </summary>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            foreach (var handler in _handlers)
            {
                var result = await handler.Handle(request, ex, cancellationToken);
                if (result.IsHandled)
                {
                    return result.Response;
                }
            }

            throw;
        }
    }
}
