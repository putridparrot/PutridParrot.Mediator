namespace PutridParrot.Mediator;

/// <summary>
/// Defines a mediator for dispatching requests to their corresponding handlers and publishing notifications.
/// Implements the mediator pattern to decouple request senders from request handlers.
/// </summary>
/// <remarks>
/// Combines <see cref="ISender"/> and <see cref="IPublisher"/>. Classes that only need one of them
/// can depend on the narrower interface; all three are registered by <c>AddMediator</c>.
/// </remarks>
public interface IMediator : ISender, IPublisher;
