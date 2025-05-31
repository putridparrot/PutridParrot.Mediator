namespace PutridParrot.Mediator;

/// <summary>
/// Composite dispatcher that delegates to multiple generated dispatchers.
/// Useful when handlers are spread across multiple assemblies, each with their own generated dispatcher.
/// </summary>
public sealed class CompositeRequestDispatcher : IRequestDispatcher
{
    private readonly IReadOnlyList<IRequestDispatcher> _dispatchers;

    public CompositeRequestDispatcher(IEnumerable<IRequestDispatcher> dispatchers)
    {
        _dispatchers = dispatchers.ToList();
    }

    public async Task<TResponse> Dispatch<TResponse>(
        IRequest<TResponse> request, 
        object handler, 
        CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();
        
        foreach (var dispatcher in _dispatchers)
        {
            try
            {
                return await dispatcher.Dispatch(request, handler, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No dispatcher case"))
            {
                // This dispatcher doesn't know about this request type, try next
                exceptions.Add(ex);
                continue;
            }
        }
        
        throw new InvalidOperationException(
            $"No dispatcher could handle request type {request.GetType().FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }

    public async Task Dispatch(
        IRequest request, 
        object handler, 
        CancellationToken cancellationToken)
    {
        var exceptions = new List<Exception>();
        
        foreach (var dispatcher in _dispatchers)
        {
            try
            {
                await dispatcher.Dispatch(request, handler, cancellationToken);
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No dispatcher case"))
            {
                // This dispatcher doesn't know about this request type, try next
                exceptions.Add(ex);
                continue;
            }
        }
        
        throw new InvalidOperationException(
            $"No dispatcher could handle request type {request.GetType().FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }
}
