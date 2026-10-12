using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class PipelineBehaviorTests
{
    private static IMediator CreateMediator(Action<MediatorConfiguration> configure, PipelineLog log)
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton(log);
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PipelineBehaviorTests).Assembly);
            configure(cfg);
        });

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Test]
    public async Task AddMediator_WithOpenBehaviors_ShouldRunInRegistrationOrder()
    {
        var log = new PipelineLog();
        var mediator = CreateMediator(cfg =>
        {
            cfg.AddOpenBehavior(typeof(OuterBehavior<,>));
            cfg.AddOpenBehavior(typeof(InnerBehavior<,>));
        }, log);

        var result = await mediator.Send(new PipelinePing("hi"));

        Assert.That(result, Is.EqualTo("hi"));
        Assert.That(log.Entries, Is.EqualTo(new[] { "outer:before", "inner:before", "handler", "inner:after", "outer:after" }));
    }

    [Test]
    public async Task AddMediator_WithClosedBehavior_ShouldOnlyApplyToItsRequestType()
    {
        var log = new PipelineLog();
        var mediator = CreateMediator(cfg => cfg.AddBehavior<PingOnlyBehavior>(), log);

        await mediator.Send(new PipelinePing("a"));
        await mediator.Send(new PipelinePong(1));

        Assert.That(log.Entries, Is.EqualTo(new[] { "ping-only", "handler", "handler" }));
    }

    [Test]
    public async Task AddMediator_WithCachingBehavior_ShouldCacheResponse()
    {
        var log = new PipelineLog();
        var mediator = CreateMediator(cfg => cfg.AddOpenBehavior(typeof(CachingBehavior<,>)), log);

        await mediator.Send(new PipelineCachedQuery(5));
        await mediator.Send(new PipelineCachedQuery(5));

        Assert.That(log.Entries, Has.Count.EqualTo(1), "Second call should be served from the cache");
    }

    [Test]
    public void AddOpenBehavior_WithClosedType_ShouldThrow()
    {
        var configuration = new MediatorConfiguration();

        Assert.Throws<ArgumentException>(() => configuration.AddOpenBehavior(typeof(PingOnlyBehavior)));
        Assert.Throws<ArgumentException>(() => configuration.AddOpenBehavior(typeof(List<>)));
    }

    [Test]
    public void AddBehavior_WithTypeNotImplementingBehavior_ShouldThrow()
    {
        var configuration = new MediatorConfiguration();

        Assert.Throws<ArgumentException>(() => configuration.AddBehavior<PipelineLog>());
    }
}

public class PipelineLog
{
    public List<string> Entries { get; } = [];
}

public record PipelinePing(string Message) : IRequest<string>;

public record PipelinePong(int Value) : IRequest<int>;

public record PipelineCachedQuery(int Id) : IRequest<int>, ICacheableRequest<int>
{
    public string GetCacheKey() => $"pipeline:{Id}";
    public TimeSpan GetCacheExpiration() => TimeSpan.FromMinutes(5);
}

public class PipelinePingHandler(PipelineLog log) : IRequestHandler<PipelinePing, string>
{
    public Task<string> Handle(PipelinePing request, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("handler");
        return Task.FromResult(request.Message);
    }
}

public class PipelinePongHandler(PipelineLog log) : IRequestHandler<PipelinePong, int>
{
    public Task<int> Handle(PipelinePong request, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("handler");
        return Task.FromResult(request.Value);
    }
}

public class PipelineCachedQueryHandler(PipelineLog log) : IRequestHandler<PipelineCachedQuery, int>
{
    public Task<int> Handle(PipelineCachedQuery request, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("handler");
        return Task.FromResult(request.Id);
    }
}

public class OuterBehavior<TRequest, TResponse>(PipelineLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("outer:before");
        var response = await next();
        log.Entries.Add("outer:after");
        return response;
    }
}

public class InnerBehavior<TRequest, TResponse>(PipelineLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("inner:before");
        var response = await next();
        log.Entries.Add("inner:after");
        return response;
    }
}

public class PingOnlyBehavior(PipelineLog log) : IPipelineBehavior<PipelinePing, string>
{
    public Task<string> Handle(PipelinePing request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("ping-only");
        return next();
    }
}
