namespace PutridParrot.Mediator;

/// <summary>
/// Defines a handler for a notification.
/// Multiple handlers can be registered for the same notification type.
/// </summary>
/// <typeparam name="TNotification">The type of notification to handle.</typeparam>
/// <remarks>
/// Unlike request handlers, notification handlers:
/// - Can have multiple handlers for the same notification
/// - Are executed in sequence (not parallel by default)
/// - Do not return values
/// - All handlers are always executed (errors don't stop propagation by default)
/// </remarks>
/// <example>
/// <code>
/// public class SendEmailOnOrderCreated : INotificationHandler&lt;OrderCreatedNotification&gt;
/// {
///     public async Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
///     {
///         await _emailService.SendOrderConfirmation(notification.OrderId);
///     }
/// }
/// </code>
/// </example>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the notification.
    /// </summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
