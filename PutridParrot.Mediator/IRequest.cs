namespace PutridParrot.Mediator;

/// <summary>
/// Marker interface for requests that do not return a response.
/// Implement this interface on request types that represent commands or actions without a return value.
/// </summary>
/// <remarks>
/// This interface is used by the mediator pattern to identify request objects that should be
/// dispatched to handlers implementing <see cref="IRequestHandler{TRequest}"/>.
/// </remarks>
public interface IRequest;

/// <summary>
/// Marker interface for requests that return a response of type <typeparamref name="TResponse"/>.
/// Implement this interface on request types that represent queries or commands that return a value.
/// </summary>
/// <typeparam name="TResponse">The type of response that will be returned by the handler.</typeparam>
/// <remarks>
/// This interface is used by the mediator pattern to identify request objects that should be
/// dispatched to handlers implementing <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// The response type is covariant, allowing for flexible type hierarchies.
/// </remarks>
public interface IRequest<out TResponse> : IRequest;