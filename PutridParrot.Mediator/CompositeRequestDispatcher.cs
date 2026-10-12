namespace PutridParrot.Mediator;

/// <summary>
/// Composite dispatcher that delegates to multiple generated dispatchers.
/// Useful when handlers are spread across multiple assemblies, each with their own generated dispatcher.
/// </summary>
public sealed class CompositeRequestDispatcher(IEnumerable<IRequestDispatcher> dispatchers) : IRequestDispatcher
{
    private readonly IReadOnlyList<IRequestDispatcher> _dispatchers = dispatchers.ToList();

    /// <summary>
    /// Whether any of the inner dispatchers can dispatch the request type.
    /// </summary>
    public bool CanDispatch(Type requestType) => _dispatchers.Any(d => d.CanDispatch(requestType));

    /// <summary>
    /// Dispatches the request using the first inner dispatcher that handles its type.
    /// </summary>
    /// <exception cref="InvalidOperationException">No inner dispatcher handles the request type.</exception>
    public async Task<TResponse> Dispatch<TResponse>(
        IRequest<TResponse> request,
        object handler,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();

        foreach (var dispatcher in _dispatchers)
        {
            if (!dispatcher.CanDispatch(requestType))
                continue;

            try
            {
                return await dispatcher.Dispatch(request, handler, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No dispatcher case"))
            {
                // This dispatcher doesn't know about this request type, try next
            }
        }

        throw new InvalidOperationException(
            $"No dispatcher could handle request type {requestType.FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }

    /// <summary>
    /// Dispatches the request using the first inner dispatcher that handles its type.
    /// </summary>
    /// <exception cref="InvalidOperationException">No inner dispatcher handles the request type.</exception>
    public async Task Dispatch(
        IRequest request,
        object handler,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();

        foreach (var dispatcher in _dispatchers)
        {
            if (!dispatcher.CanDispatch(requestType))
                continue;

            try
            {
                await dispatcher.Dispatch(request, handler, cancellationToken);
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No dispatcher case"))
            {
                // This dispatcher doesn't know about this request type, try next
            }
        }

        throw new InvalidOperationException(
            $"No dispatcher could handle request type {requestType.FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }
}
