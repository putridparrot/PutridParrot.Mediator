namespace PutridParrot.Mediator;

/// <summary>
/// Specifies the lifetime of request handlers when registered with dependency injection.
/// </summary>
public enum HandlerLifetime
{
    /// <summary>
    /// A new instance is created for each request. This is the default.
    /// Suitable for stateless handlers.
    /// </summary>
    Transient,
    
    /// <summary>
    /// A single instance is created per scope (e.g., per HTTP request in ASP.NET Core).
    /// Suitable for handlers that need to share state within a request scope.
    /// </summary>
    Scoped,
    
    /// <summary>
    /// A single instance is created and shared across all requests.
    /// Use with caution - handlers must be thread-safe.
    /// </summary>
    Singleton
}

/// <summary>
/// Options for configuring mediator behavior.
/// </summary>
public class MediatorOptions
{
    /// <summary>
    /// Default lifetime for request handlers.
    /// Default is Transient.
    /// </summary>
    public HandlerLifetime DefaultHandlerLifetime { get; set; } = HandlerLifetime.Transient;
    
    /// <summary>
    /// Strategy for handling exceptions in notification handlers.
    /// </summary>
    public NotificationExceptionHandlingStrategy NotificationExceptionStrategy { get; set; } = NotificationExceptionHandlingStrategy.ContinueOnException;
}

/// <summary>
/// Strategy for handling exceptions in notification handlers.
/// </summary>
public enum NotificationExceptionHandlingStrategy
{
    /// <summary>
    /// Continue executing remaining handlers even if one throws.
    /// All exceptions are collected and can be accessed.
    /// </summary>
    ContinueOnException,
    
    /// <summary>
    /// Stop executing remaining handlers if one throws an exception.
    /// The exception is propagated immediately.
    /// </summary>
    StopOnException
}
