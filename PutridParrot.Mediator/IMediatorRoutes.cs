using System.ComponentModel;

namespace PutridParrot.Mediator;

/// <summary>
/// Reflection-free routing from a request, stream or notification to strongly-typed <see cref="MediatorRuntime"/> calls.
/// </summary>
/// <remarks>
/// Implemented by the source generator (one <c>GeneratedMediatorRoutes</c> class per project), not by user code.
/// Each method returns <c>null</c> when the type is unknown to this implementation, and the mediator then
/// tries the next implementation, falling back to runtime-built delegates if none knows the type.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMediatorRoutes
{
    /// <summary>
    /// Sends a request, or returns <c>null</c> if the request type is not known.
    /// </summary>
    Task<TResponse>? Send<TResponse>(IRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a command, or returns <c>null</c> if the command type is not known.
    /// </summary>
    Task? Send(IRequest request, MediatorContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a stream, or returns <c>null</c> if the streaming request type is not known.
    /// </summary>
    IAsyncEnumerable<TResponse>? CreateStream<TResponse>(IStreamRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a notification, or returns <c>null</c> if the notification type is not known.
    /// </summary>
    Task? Publish(INotification notification, MediatorContext context, CancellationToken cancellationToken);
}
