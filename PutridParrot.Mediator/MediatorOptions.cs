namespace PutridParrot.Mediator;

/// <summary>
/// Runtime options used by the <see cref="Mediator"/>.
/// </summary>
/// <remarks>
/// Registered by <c>AddMediator</c> from the values set on <see cref="MediatorConfiguration"/>.
/// </remarks>
public sealed class MediatorOptions
{
    /// <summary>
    /// How exceptions thrown by notification handlers are handled during <see cref="IMediator.Publish"/>.
    /// Default is <see cref="global::PutridParrot.Mediator.NotificationErrorHandling.ThrowAfterAllHandlers"/>.
    /// </summary>
    public NotificationErrorHandling NotificationErrorHandling { get; init; } = NotificationErrorHandling.ThrowAfterAllHandlers;
}

/// <summary>
/// Strategy for handling exceptions thrown by notification handlers.
/// Handlers are always invoked one at a time, in registration order.
/// </summary>
public enum NotificationErrorHandling
{
    /// <summary>
    /// Every handler is invoked even if an earlier one fails. Afterwards the exception is rethrown,
    /// or an <see cref="AggregateException"/> is thrown if more than one handler failed. This is the default.
    /// </summary>
    ThrowAfterAllHandlers,

    /// <summary>
    /// The first exception is rethrown immediately and the remaining handlers are not invoked.
    /// </summary>
    StopOnFirstException,

    /// <summary>
    /// Exceptions are logged (when an <c>ILogger</c> is available) and not rethrown,
    /// so a failing handler never fails the publisher.
    /// </summary>
    LogAndContinue
}
