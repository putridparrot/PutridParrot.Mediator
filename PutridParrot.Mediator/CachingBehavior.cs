using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator;

/// <summary>
/// Pipeline behavior that caches responses for requests implementing <see cref="ICacheableRequest{TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response being returned.</typeparam>
/// <remarks>
/// <para>
/// This behavior intercepts requests that implement <see cref="ICacheableRequest{TResponse}"/>
/// and caches their responses based on the cache key and expiration provided by the request.
/// </para>
/// <para>
/// If a cached response exists, the handler is not invoked, providing significant performance improvements
/// for expensive operations like database queries or external API calls.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Register the caching behavior
/// services.AddMemoryCache();
/// services.AddMediator(cfg =>
/// {
///     cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
///     cfg.AddOpenBehavior(typeof(CachingBehavior&lt;,&gt;));
/// });
/// 
/// // Create a cacheable request
/// public record GetProductQuery(int Id) : IRequest&lt;Product&gt;, ICacheableRequest&lt;Product&gt;
/// {
///     public string GetCacheKey() => $"product:{Id}";
///     public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
/// }
/// 
/// // Use it
/// var product = await mediator.Send(new GetProductQuery(123)); // Hits handler
/// var cached = await mediator.Send(new GetProductQuery(123));  // Returns cached result
/// </code>
/// </example>
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the CachingBehavior class.
    /// </summary>
    /// <param name="cache">The memory cache instance for storing responses.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    public CachingBehavior(
        IMemoryCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the request with caching logic.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="next">The next handler or behavior in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, either from cache or from the handler.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only cache requests that implement ICacheableRequest
        if (request is not ICacheableRequest<TResponse> cacheableRequest)
        {
            _logger.LogTrace(
                "Request {RequestType} does not implement ICacheableRequest, skipping cache",
                typeof(TRequest).Name);
            return await next();
        }

        var cacheKey = cacheableRequest.GetCacheKey();
        
        // Try to get from cache
        if (_cache.TryGetValue<TResponse>(cacheKey, out var cachedResponse))
        {
            _logger.LogDebug(
                "Cache hit for {RequestType} with key {CacheKey}",
                typeof(TRequest).Name,
                cacheKey);
            
            return cachedResponse!;
        }

        _logger.LogDebug(
            "Cache miss for {RequestType} with key {CacheKey}, executing handler",
            typeof(TRequest).Name,
            cacheKey);

        // Execute the handler
        var response = await next();

        // Cache the response
        var expiration = cacheableRequest.GetCacheExpiration();
        
        if (expiration > TimeSpan.Zero)
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            };

            _cache.Set(cacheKey, response, cacheEntryOptions);
            
            _logger.LogDebug(
                "Cached response for {RequestType} with key {CacheKey}, expires in {Expiration}",
                typeof(TRequest).Name,
                cacheKey,
                expiration);
        }
        else
        {
            _logger.LogWarning(
                "Request {RequestType} returned zero expiration, response not cached",
                typeof(TRequest).Name);
        }

        return response;
    }
}
