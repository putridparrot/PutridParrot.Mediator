namespace PutridParrot.Mediator;

/// <summary>
/// Publishes notifications to all of their handlers.
/// Depend on this instead of <see cref="IMediator"/> when a class only publishes notifications.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// All handlers are invoked sequentially, in registration order. By default, if a handler throws,
    /// the remaining handlers are still executed and the exception is rethrown afterwards
    /// (an <see cref="AggregateException"/> if several handlers failed).
    /// This is configurable with <see cref="MediatorConfiguration.NotificationErrorHandling"/>.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is null.</exception>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}
