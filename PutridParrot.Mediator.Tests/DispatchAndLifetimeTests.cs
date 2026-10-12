using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class DispatchAndLifetimeTests
{
    [Test]
    public async Task Send_FromScope_ShouldResolveHandlerWithScopedDependency()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedCounter>();
        services.AddMediator(typeof(DispatchAndLifetimeTests).Assembly);

        // ValidateScopes makes resolving a scoped service from the root provider throw
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var counter = scope.ServiceProvider.GetRequiredService<ScopedCounter>();

            Assert.That(await mediator.Send(new UsesScopedServiceRequest()), Is.EqualTo(1));
            Assert.That(await mediator.Send(new UsesScopedServiceRequest()), Is.EqualTo(2));
            Assert.That(counter.Value, Is.EqualTo(2), "Handler should share the scope's instance");
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            Assert.That(await mediator.Send(new UsesScopedServiceRequest()), Is.EqualTo(1), "A new scope gets a new instance");
        }
    }

    [Test]
    public async Task Mediator_WhenNoRoutesKnowTheType_ShouldFallBackToRuntimeDelegates()
    {
        var (mediator, log, _) = CreateMediatorWithRoutes(new EmptyRoutes());

        await AssertAllOperationsWork(mediator, log);
    }

    [Test]
    public async Task Mediator_ShouldUseTheFirstRoutesThatKnowTheType()
    {
        var recording = new RecordingRoutes();
        var (mediator, log, _) = CreateMediatorWithRoutes(new EmptyRoutes(), recording);

        await AssertAllOperationsWork(mediator, log);

        Assert.That(recording.Routed, Is.EqualTo(new[] { "request", "command", "stream", "notification" }));
    }

    [Test]
    public void Mediator_Constructor_WithNullServiceProvider_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new Mediator(null!));
    }

    [Test]
    public void Mediator_Constructor_WithoutRoutes_ShouldSucceed()
    {
        Assert.DoesNotThrow(() => new Mediator(new ServiceCollection().BuildServiceProvider()));
    }

    private static (IMediator Mediator, NotificationLog Log, IServiceProvider Provider) CreateMediatorWithRoutes(params IMediatorRoutes[] routes)
    {
        var log = new NotificationLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddMediator(typeof(DispatchAndLifetimeTests).Assembly);
        foreach (var route in routes)
        {
            services.AddSingleton(route);
        }

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), log, provider);
    }

    private static async Task AssertAllOperationsWork(IMediator mediator, NotificationLog log)
    {
        Assert.That(await mediator.Send(new FallbackRequest(3)), Is.EqualTo(6));
        await mediator.Send(new FallbackCommand());

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new FallbackStreamRequest(3)))
        {
            items.Add(item);
        }
        Assert.That(items, Is.EqualTo(new[] { 0, 1, 2 }));

        await mediator.Publish(new RoutedNotification());
        Assert.That(log.Entries, Is.EqualTo(new[] { "routed notification" }));
    }

    [Test]
    public void Send_WhenHandlerThrowsSynchronously_ShouldPropagateOriginalException()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(DispatchAndLifetimeTests).Assembly);
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        // Non-async handlers throw before returning a Task; this must not surface as TargetInvocationException
        var ex = Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new ThrowsSynchronouslyRequest()));
        Assert.That(ex!.Message, Is.EqualTo("sync failure"));

        Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new ThrowsSynchronouslyCommand()));
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new ThrowsSynchronouslyStream()))
            {
            }
        });
    }

}

public class ScopedCounter
{
    public int Value;
}

public record UsesScopedServiceRequest : IRequest<int>;

public class UsesScopedServiceHandler(ScopedCounter counter) : IRequestHandler<UsesScopedServiceRequest, int>
{
    public Task<int> Handle(UsesScopedServiceRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(++counter.Value);
    }
}

public record FallbackRequest(int Value) : IRequest<int>;

public record FallbackCommand : IRequest;

public record FallbackStreamRequest(int Count) : IStreamRequest<int>;

public class FallbackRequestHandler : IRequestHandler<FallbackRequest, int>
{
    public Task<int> Handle(FallbackRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(request.Value * 2);
}

public class FallbackCommandHandler : IRequestHandler<FallbackCommand>
{
    public Task Handle(FallbackCommand request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class FallbackStreamHandler : IStreamRequestHandler<FallbackStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(FallbackStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public record ThrowsSynchronouslyRequest : IRequest<int>;

public record ThrowsSynchronouslyCommand : IRequest;

public record ThrowsSynchronouslyStream : IStreamRequest<int>;

public class ThrowsSynchronouslyRequestHandler : IRequestHandler<ThrowsSynchronouslyRequest, int>
{
    public Task<int> Handle(ThrowsSynchronouslyRequest request, CancellationToken cancellationToken = default) =>
        throw new ArgumentException("sync failure");
}

public class ThrowsSynchronouslyCommandHandler : IRequestHandler<ThrowsSynchronouslyCommand>
{
    public Task Handle(ThrowsSynchronouslyCommand request, CancellationToken cancellationToken = default) =>
        throw new ArgumentException("sync failure");
}

public class ThrowsSynchronouslyStreamHandler : IStreamRequestHandler<ThrowsSynchronouslyStream, int>
{
    public IAsyncEnumerable<int> Handle(ThrowsSynchronouslyStream request, CancellationToken cancellationToken = default) =>
        throw new ArgumentException("sync failure");
}

public record RoutedNotification : INotification;

public class RoutedNotificationHandler(NotificationLog log) : INotificationHandler<RoutedNotification>
{
    public Task Handle(RoutedNotification notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("routed notification");
        return Task.CompletedTask;
    }
}

/// <summary>Routes that know no types, like the generated routes of another project.</summary>
public class EmptyRoutes : IMediatorRoutes
{
    public Task<TResponse>? Send<TResponse>(IRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken) => null;

    public Task? Send(IRequest request, MediatorContext context, CancellationToken cancellationToken) => null;

    public IAsyncEnumerable<TResponse>? CreateStream<TResponse>(IStreamRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken) => null;

    public Task? Publish(INotification notification, MediatorContext context, CancellationToken cancellationToken) => null;
}

/// <summary>Hand-written equivalent of generated routes for the Fallback* types, recording what it routed.</summary>
public class RecordingRoutes : IMediatorRoutes
{
    public List<string> Routed { get; } = [];

    public Task<TResponse>? Send<TResponse>(IRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken)
    {
        if (request is not FallbackRequest fallback || typeof(TResponse) != typeof(int))
            return null;

        Routed.Add("request");
        return (Task<TResponse>)(object)MediatorRuntime.Send<FallbackRequest, int>(context, fallback, cancellationToken);
    }

    public Task? Send(IRequest request, MediatorContext context, CancellationToken cancellationToken)
    {
        if (request is not FallbackCommand command)
            return null;

        Routed.Add("command");
        return MediatorRuntime.SendCommand(context, command, cancellationToken);
    }

    public IAsyncEnumerable<TResponse>? CreateStream<TResponse>(IStreamRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken)
    {
        if (request is not FallbackStreamRequest stream || typeof(TResponse) != typeof(int))
            return null;

        Routed.Add("stream");
        return (IAsyncEnumerable<TResponse>)MediatorRuntime.CreateStream<FallbackStreamRequest, int>(context, stream, cancellationToken);
    }

    public Task? Publish(INotification notification, MediatorContext context, CancellationToken cancellationToken)
    {
        if (notification is not RoutedNotification routed)
            return null;

        Routed.Add("notification");
        return MediatorRuntime.Publish(context, routed, cancellationToken);
    }
}
