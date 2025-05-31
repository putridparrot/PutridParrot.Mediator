namespace PutridParrot.Mediator;

/// <summary>
/// Marker interface for notifications that can be published to multiple handlers.
/// Unlike requests, notifications can have zero or more handlers.
/// </summary>
/// <remarks>
/// Use notifications for:
/// - Domain events (e.g., OrderCreated, UserRegistered)
/// - Broadcasting changes to multiple subscribers
/// - Implementing event-driven architectures
/// </remarks>
/// <example>
/// <code>
/// public record OrderCreatedNotification(int OrderId, decimal Amount) : INotification;
/// </code>
/// </example>
public interface INotification
{
}
