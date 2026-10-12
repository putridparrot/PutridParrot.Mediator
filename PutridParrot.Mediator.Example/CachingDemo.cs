using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Example.Caching;

/// <summary>
/// Demonstrates CachingBehavior with cacheable and non-cacheable requests.
/// </summary>
public static class CachingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║  Caching Behavior - Demo Program          ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");

        // Setup
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddMemoryCache();
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
            // Caching behavior executes before the handler
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        Console.WriteLine("=== Example 1: Cacheable Query ===\n");

        // First request - will hit the handler
        Console.WriteLine("🔍 First request (cache miss):");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var user1 = await mediator.Send(new GetUserQuery(UserId: 1));
        stopwatch.Stop();
        Console.WriteLine($"   Result: {user1}");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms\n");

        // Second request - will use cache
        Console.WriteLine("🔍 Second request (cache hit):");
        stopwatch.Restart();
        var user2 = await mediator.Send(new GetUserQuery(UserId: 1));
        stopwatch.Stop();
        Console.WriteLine($"   Result: {user2}");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms (from cache!)\n");

        // Different ID - will hit handler again
        Console.WriteLine("🔍 Different user ID (cache miss):");
        stopwatch.Restart();
        var user3 = await mediator.Send(new GetUserQuery(UserId: 2));
        stopwatch.Stop();
        Console.WriteLine($"   Result: {user3}");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms\n");

        Console.WriteLine("=== Example 2: Non-Cacheable Query ===\n");

        // This query doesn't implement ICacheableRequest
        Console.WriteLine("🔍 Non-cached query:");
        var order1 = await mediator.Send(new GetOrderQuery(OrderId: 100));
        Console.WriteLine($"   Result: {order1}");
        Console.WriteLine("   (Not cached - implements IRequest but not ICacheableRequest)\n");

        Console.WriteLine("=== Example 3: Cache Expiration ===\n");

        // Request with short expiration
        Console.WriteLine("🔍 Query with 2-second cache:");
        var weather1 = await mediator.Send(new GetWeatherQuery(City: "London"));
        Console.WriteLine($"   Result: {weather1}");

        Console.WriteLine("\n⏱️  Waiting 1 second...");
        await Task.Delay(1000);

        Console.WriteLine("🔍 Same query (within cache window):");
        var weather2 = await mediator.Send(new GetWeatherQuery(City: "London"));
        Console.WriteLine($"   Result: {weather2} (from cache)");

        Console.WriteLine("\n⏱️  Waiting 2 more seconds (total 3s, cache expires)...");
        await Task.Delay(2000);

        Console.WriteLine("🔍 Same query (cache expired):");
        var weather3 = await mediator.Send(new GetWeatherQuery(City: "London"));
        Console.WriteLine($"   Result: {weather3} (fresh data)\n");

        Console.WriteLine("✅ All caching examples completed!\n");
    }
}

// ========================================
// CACHEABLE REQUESTS
// ========================================

/// <summary>
/// Cacheable query - implements ICacheableRequest.
/// </summary>
public record GetUserQuery(int UserId) : IRequest<UserDto>, ICacheableRequest<UserDto>
{
    public string GetCacheKey() => $"user:{UserId}";
    
    public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
}

public record UserDto(int Id, string Name, string Email);

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"   [Handler] Fetching user {request.UserId} from database...");
        
        // Simulate slow database query
        await Task.Delay(500, cancellationToken);
        
        return new UserDto(
            request.UserId, 
            $"User {request.UserId}", 
            $"user{request.UserId}@example.com");
    }
}

/// <summary>
/// Non-cacheable query - only implements IRequest.
/// </summary>
public record GetOrderQuery(int OrderId) : IRequest<OrderDto>;

public record OrderDto(int Id, decimal Total);

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"   [Handler] Fetching order {request.OrderId} (not cached)...");
        await Task.Delay(200, cancellationToken);
        return new OrderDto(request.OrderId, 99.99m);
    }
}

/// <summary>
/// Cacheable query with short expiration.
/// </summary>
public record GetWeatherQuery(string City) : IRequest<WeatherDto>, ICacheableRequest<WeatherDto>
{
    public string GetCacheKey() => $"weather:{City.ToLowerInvariant()}";
    
    public TimeSpan GetCacheExpiration() => TimeSpan.FromSeconds(2);
}

public record WeatherDto(string City, int Temperature, string Condition);

public class GetWeatherQueryHandler : IRequestHandler<GetWeatherQuery, WeatherDto>
{
    private static readonly Random _random = new();
    
    public async Task<WeatherDto> Handle(GetWeatherQuery request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"   [Handler] Fetching weather for {request.City} from API...");
        
        await Task.Delay(300, cancellationToken);
        
        var temp = _random.Next(15, 30);
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Windy" };
        var condition = conditions[_random.Next(conditions.Length)];
        
        return new WeatherDto(request.City, temp, condition);
    }
}
