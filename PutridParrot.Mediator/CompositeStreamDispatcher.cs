namespace PutridParrot.Mediator;

/// <summary>
/// Composite stream dispatcher that delegates to multiple generated stream dispatchers.
/// Used when stream handlers are spread across multiple assemblies, each with their own generated dispatcher.
/// </summary>
public sealed class CompositeStreamDispatcher(IEnumerable<IStreamDispatcher> dispatchers) : IStreamDispatcher
{
    private readonly IReadOnlyList<IStreamDispatcher> _dispatchers = dispatchers.ToList();

    /// <summary>
    /// Whether any of the inner dispatchers can dispatch the streaming request type.
    /// </summary>
    public bool CanDispatch(Type requestType) => _dispatchers.Any(d => d.CanDispatch(requestType));

    /// <summary>
    /// Dispatches the streaming request using the first dispatcher that knows its request type.
    /// </summary>
    /// <exception cref="InvalidOperationException">No dispatcher handles the request type.</exception>
    public IAsyncEnumerable<TResponse> DispatchStream<TResponse>(
        IStreamRequest<TResponse> request,
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
                // Generated dispatchers select the handler eagerly and throw before enumeration
                // if they don't know the request type, so the stream itself is not started here
                return dispatcher.DispatchStream(request, handler, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No stream dispatcher case"))
            {
                // This dispatcher doesn't know about this request type, try next
            }
        }

        throw new InvalidOperationException(
            $"No stream dispatcher could handle request type {requestType.FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }
}
