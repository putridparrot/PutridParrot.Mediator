namespace PutridParrot.Mediator;

/// <summary>
/// Marker interface for requests that support response caching.
/// Implement this interface on requests to enable automatic caching via CachingBehavior.
/// </summary>
/// <typeparam name="TResponse">The type of response to cache.</typeparam>
/// <remarks>
/// Requests implementing this interface will have their responses cached automatically
/// when CachingBehavior is registered in the pipeline.
/// </remarks>
/// <example>
/// <code>
/// public record GetUserQuery(int UserId) : IRequest&lt;UserDto&gt;, ICacheableRequest&lt;UserDto&gt;
/// {
///     public string GetCacheKey() => $"user:{UserId}";
///     public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
/// }
/// </code>
/// </example>
public interface ICacheableRequest<TResponse>
{
    /// <summary>
    /// Gets the unique cache key for this request.
    /// </summary>
    /// <returns>A unique string that identifies this request in the cache.</returns>
    /// <remarks>
    /// The cache key should be unique for different parameter combinations.
    /// Include all relevant request data in the key to avoid cache collisions.
    /// </remarks>
    string GetCacheKey();
    
    /// <summary>
    /// Gets the duration for which the response should be cached.
    /// </summary>
    /// <returns>The cache expiration timespan.</returns>
    /// <remarks>
    /// Return <see cref="TimeSpan.Zero"/> to cache indefinitely (not recommended).
    /// Consider business requirements when setting expiration times.
    /// </remarks>
    TimeSpan GetCacheExpiration();
}
