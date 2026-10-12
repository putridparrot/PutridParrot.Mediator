namespace PutridParrot.Mediator;

/// <summary>
/// Pipeline behavior for streaming requests. Wraps the stream returned by the next behavior or the handler,
/// so it can act before enumeration starts, on each item, and when the stream completes or fails.
/// </summary>
/// <typeparam name="TRequest">The type of streaming request.</typeparam>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
/// <remarks>
/// Register with <see cref="MediatorConfiguration.AddOpenBehavior"/> or <see cref="MediatorConfiguration.AddBehavior{TBehavior}"/>,
/// like request behaviors. Stream behaviors run in the order they are added; the first added is the outermost.
/// Implementations are usually async iterators: <c>await foreach (var item in next()) yield return item;</c>
/// </remarks>
/// <example>
/// <code>
/// public class CountingStreamBehavior&lt;TRequest, TResponse&gt; : IStreamPipelineBehavior&lt;TRequest, TResponse&gt;
///     where TRequest : notnull
/// {
///     public async IAsyncEnumerable&lt;TResponse&gt; Handle(
///         TRequest request,
///         StreamHandlerDelegate&lt;TResponse&gt; next,
///         [EnumeratorCancellation] CancellationToken cancellationToken)
///     {
///         var count = 0;
///         await foreach (var item in next().WithCancellation(cancellationToken))
///         {
///             count++;
///             yield return item;
///         }
///         Console.WriteLine($"{typeof(TRequest).Name} streamed {count} items");
///     }
/// }
/// </code>
/// </example>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Handles the streaming request, calling <paramref name="next"/> to get the stream from the next
    /// behavior or the handler.
    /// </summary>
    /// <param name="request">The streaming request.</param>
    /// <param name="next">Delegate returning the stream of the next behavior or the handler.</param>
    /// <param name="cancellationToken">Cancellation token for the stream.</param>
    /// <returns>The stream of items, possibly filtered, transformed or observed.</returns>
    IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// Delegate that returns the stream of the next stream behavior or the handler.
/// </summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>();
