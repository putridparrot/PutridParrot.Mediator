using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

/// <summary>
/// Unit tests for CachingBehavior that test the behavior in isolation.
/// These tests directly invoke the behavior without relying on the Mediator pipeline infrastructure.
/// </summary>
[TestFixture]
public class CachingBehaviorTests
{
    [Test]
    public async Task CachingBehavior_WithCacheableRequest_ShouldCacheResponse()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new LoggerFactory().CreateLogger<CachingBehavior<TestCacheableQuery, TestResult>>();
        var cachingBehavior = new CachingBehavior<TestCacheableQuery, TestResult>(cache, logger);
        
        var handler = new TestCacheableQueryHandler();
        var request = new TestCacheableQuery(42);
        
        var handlerCallCount = 0;
        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            handlerCallCount++;
            return handler.Handle(request);
        };
        
        // Act
        var result1 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        var result2 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        
        // Assert
        Assert.That(result1.Value, Is.EqualTo(42));
        Assert.That(result2.Value, Is.EqualTo(42));
        Assert.That(handlerCallCount, Is.EqualTo(1), "Handler should only be called once - second call uses cache");
    }

    [Test]
    public async Task CachingBehavior_WithNonCacheableRequest_ShouldNotCache()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new LoggerFactory().CreateLogger<CachingBehavior<TestNonCacheableQuery, TestResult>>();
        var cachingBehavior = new CachingBehavior<TestNonCacheableQuery, TestResult>(cache, logger);
        
        var handler = new TestNonCacheableQueryHandler();
        var request = new TestNonCacheableQuery(42);
        
        var handlerCallCount = 0;
        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            handlerCallCount++;
            return handler.Handle(request);
        };
        
        // Act
        var result1 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        var result2 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        
        // Assert
        Assert.That(handlerCallCount, Is.EqualTo(2), 
            "Handler should be called twice - caching not applied to non-cacheable requests");
    }

    [Test]
    public async Task CachingBehavior_WithDifferentCacheKeys_ShouldCacheSeparately()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new LoggerFactory().CreateLogger<CachingBehavior<TestCacheableQuery, TestResult>>();
        var cachingBehavior = new CachingBehavior<TestCacheableQuery, TestResult>(cache, logger);
        
        var handler = new TestCacheableQueryHandler();
        var handlerCallCount = 0;
        
        // Act - First unique key
        var request1 = new TestCacheableQuery(1);
        RequestHandlerDelegate<TestResult> delegate1 = () =>
        {
            handlerCallCount++;
            return handler.Handle(request1);
        };
        var result1 = await cachingBehavior.Handle(request1, delegate1, CancellationToken.None);
        
        // Act - Second unique key
        var request2 = new TestCacheableQuery(2);
        RequestHandlerDelegate<TestResult> delegate2 = () =>
        {
            handlerCallCount++;
            return handler.Handle(request2);
        };
        var result2 = await cachingBehavior.Handle(request2, delegate2, CancellationToken.None);
        
        // Act - Repeat first key (should hit cache)
        var result3 = await cachingBehavior.Handle(request1, delegate1, CancellationToken.None);
        
        // Assert
        Assert.That(result1.Value, Is.EqualTo(1));
        Assert.That(result2.Value, Is.EqualTo(2));
        Assert.That(result3.Value, Is.EqualTo(1));
        Assert.That(handlerCallCount, Is.EqualTo(2), 
            "Handler should be called twice - once for each unique cache key");
    }

    [Test]
    public async Task CachingBehavior_WithExpiredCache_ShouldRefetch()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new LoggerFactory().CreateLogger<CachingBehavior<TestShortCacheQuery, TestResult>>();
        var cachingBehavior = new CachingBehavior<TestShortCacheQuery, TestResult>(cache, logger);
        
        var handler = new TestShortCacheQueryHandler();
        var request = new TestShortCacheQuery(99);
        
        var handlerCallCount = 0;
        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            handlerCallCount++;
            return handler.Handle(request);
        };
        
        // Act
        var result1 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        
        // Wait for cache to expire (100ms expiration)
        await Task.Delay(150);
        
        var result2 = await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        
        // Assert
        Assert.That(handlerCallCount, Is.EqualTo(2), 
            "Handler should be called twice - cache expired after first call");
    }

    [Test]
    public async Task CachingBehavior_WithZeroExpiration_ShouldNotCache()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new LoggerFactory().CreateLogger<CachingBehavior<TestZeroExpirationQuery, TestResult>>();
        var cachingBehavior = new CachingBehavior<TestZeroExpirationQuery, TestResult>(cache, logger);
        
        var handler = new TestZeroExpirationQueryHandler();
        var request = new TestZeroExpirationQuery(1);
        
        var handlerCallCount = 0;
        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            handlerCallCount++;
            return handler.Handle(request);
        };
        
        // Act
        await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        await cachingBehavior.Handle(request, handlerDelegate, CancellationToken.None);
        
        // Assert
        Assert.That(handlerCallCount, Is.EqualTo(2),
            "Handler should be called twice - zero expiration means no caching");
    }

    [Test]
    public async Task CachingBehavior_ThroughMediator_ShouldCacheResponse()
    {
        // Arrange - no source generator in this assembly, so behaviors must run via the Mediator itself
        var counter = new HandlerCallCounter();
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton(counter);
        services.AddMediator(typeof(CachingBehaviorTests).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result1 = await mediator.Send(new TestCountedCacheableQuery(7));
        var result2 = await mediator.Send(new TestCountedCacheableQuery(7));
        var result3 = await mediator.Send(new TestCountedCacheableQuery(8));

        // Assert
        Assert.That(result1.Value, Is.EqualTo(7));
        Assert.That(result2.Value, Is.EqualTo(7));
        Assert.That(result3.Value, Is.EqualTo(8));
        Assert.That(counter.Count, Is.EqualTo(2), "Second call for the same key should be served from the cache");
    }
}

public class HandlerCallCounter
{
    public int Count;
}

public record TestCountedCacheableQuery(int Id) : IRequest<TestResult>, ICacheableRequest<TestResult>
{
    public string GetCacheKey() => $"counted:{Id}";
    public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
}

public class TestCountedCacheableQueryHandler(HandlerCallCounter counter) : IRequestHandler<TestCountedCacheableQuery, TestResult>
{
    public Task<TestResult> Handle(TestCountedCacheableQuery request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref counter.Count);
        return Task.FromResult(new TestResult(request.Id));
    }
}

// Test request types
public record TestCacheableQuery(int Id) : IRequest<TestResult>, ICacheableRequest<TestResult>
{
    public string GetCacheKey() => $"test:{Id}";
    public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
}

public record TestNonCacheableQuery(int Id) : IRequest<TestResult>;

public record TestShortCacheQuery(int Id) : IRequest<TestResult>, ICacheableRequest<TestResult>
{
    public string GetCacheKey() => $"short:{Id}";
    public TimeSpan GetCacheExpiration() => TimeSpan.FromMilliseconds(100);
}

public record TestZeroExpirationQuery(int Id) : IRequest<TestResult>, ICacheableRequest<TestResult>
{
    public string GetCacheKey() => $"zero:{Id}";
    public TimeSpan GetCacheExpiration() => TimeSpan.Zero;
}

public record TestResult(int Value);

// Test handlers
public class TestCacheableQueryHandler : IRequestHandler<TestCacheableQuery, TestResult>
{
    public Task<TestResult> Handle(TestCacheableQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestResult(request.Id));
    }
}

public class TestNonCacheableQueryHandler : IRequestHandler<TestNonCacheableQuery, TestResult>
{
    public Task<TestResult> Handle(TestNonCacheableQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestResult(request.Id));
    }
}

public class TestShortCacheQueryHandler : IRequestHandler<TestShortCacheQuery, TestResult>
{
    public Task<TestResult> Handle(TestShortCacheQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestResult(request.Id));
    }
}

public class TestZeroExpirationQueryHandler : IRequestHandler<TestZeroExpirationQuery, TestResult>
{
    public Task<TestResult> Handle(TestZeroExpirationQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TestResult(request.Id));
    }
}
