namespace PutridParrot.Mediator;

/// <summary>
/// Defines a handler for a streaming request that returns results incrementally.
/// </summary>
/// <typeparam name="TRequest">The type of streaming request being handled.</typeparam>
/// <typeparam name="TResponse">The type of each item in the response stream.</typeparam>
/// <remarks>
/// Stream handlers should:
/// - Use `yield return` to emit items as they become available
/// - Respect the cancellation token for early termination
/// - Handle exceptions appropriately (exceptions stop the stream)
/// - Consider using `ConfigureAwait(false)` for library code
/// </remarks>
/// <example>
/// <code>
/// public class GetOrdersStreamHandler : IStreamRequestHandler&lt;GetOrdersStreamQuery, Order&gt;
/// {
///     private readonly IOrderRepository _repository;
///     
///     public GetOrdersStreamHandler(IOrderRepository repository)
///     {
///         _repository = repository;
///     }
///     
///     public async IAsyncEnumerable&lt;Order&gt; Handle(
///         GetOrdersStreamQuery request, 
///         [EnumeratorCancellation] CancellationToken cancellationToken = default)
///     {
///         var pageSize = 100;
///         var page = 0;
///         
///         while (true)
///         {
///             var orders = await _repository.GetPageAsync(
///                 request.CustomerId, 
///                 page++, 
///                 pageSize, 
///                 cancellationToken);
///             
///             if (!orders.Any())
///                 yield break;
///             
///             foreach (var order in orders)
///             {
///                 cancellationToken.ThrowIfCancellationRequested();
///                 yield return order;
///             }
///         }
///     }
/// }
/// </code>
/// </example>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles a streaming request and returns an async enumerable of responses.
    /// </summary>
    /// <param name="request">The streaming request to handle.</param>
    /// <param name="cancellationToken">Cancellation token to stop the stream early.</param>
    /// <returns>An async enumerable that yields responses as they become available.</returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}
