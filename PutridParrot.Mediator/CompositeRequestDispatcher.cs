namespace PutridParrot.Mediator;

/// <summary>
/// Composite dispatcher that delegates to multiple generated dispatchers.
/// Useful when handlers are spread across multiple assemblies, each with their own generated dispatcher.
/// </summary>
public sealed class CompositeRequestDispatcher(IEnumerable<IRequestDispatcher> dispatchers) : IRequestDispatcher
{
    private readonly IReadOnlyList<IRequestDispatcher> _dispatchers = dispatchers.ToList();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="request"></param>
    /// <param name="handler"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
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
            }
        }
        
        throw new InvalidOperationException(
            $"No dispatcher could handle request type {request.GetType().FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="request"></param>
    /// <param name="handler"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException"></exception>
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
            }
        }
        
        throw new InvalidOperationException(
            $"No dispatcher could handle request type {request.GetType().FullName}. " +
            $"Tried {_dispatchers.Count} dispatcher(s).");
    }
}
