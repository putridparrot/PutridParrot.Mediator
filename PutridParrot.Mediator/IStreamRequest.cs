namespace PutridParrot.Mediator;

/// <summary>
/// Marker interface for streaming requests that return an async enumerable stream of responses.
/// Use this for requests that return data incrementally rather than all at once.
/// </summary>
/// <typeparam name="TResponse">The type of each item in the response stream.</typeparam>
/// <remarks>
/// Streaming requests are ideal for:
/// - Large datasets that should be processed incrementally
/// - Real-time data feeds
/// - Paginated results
/// - Long-running operations with progress reporting
/// </remarks>
/// <example>
/// <code>
/// public record GetOrdersStreamQuery(int CustomerId) : IStreamRequest&lt;Order&gt;;
/// 
/// public class GetOrdersStreamHandler : IStreamRequestHandler&lt;GetOrdersStreamQuery, Order&gt;
/// {
///     public async IAsyncEnumerable&lt;Order&gt; Handle(
///         GetOrdersStreamQuery request, 
///         [EnumeratorCancellation] CancellationToken cancellationToken)
///     {
///         await foreach (var order in _database.GetOrdersAsync(request.CustomerId)
///             .WithCancellation(cancellationToken))
///         {
///             yield return order;
///         }
///     }
/// }
/// </code>
/// </example>
public interface IStreamRequest<out TResponse>
{
}
