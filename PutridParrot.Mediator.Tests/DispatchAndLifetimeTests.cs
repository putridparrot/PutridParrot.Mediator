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
    public async Task Send_WhenDispatcherCannotDispatch_ShouldFallBackToReflection()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(DispatchAndLifetimeTests).Assembly);
        services.AddSingleton<IRequestDispatcher, NoCaseRequestDispatcher>();
        services.AddSingleton<IStreamDispatcher, NoCaseStreamDispatcher>();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        Assert.That(await mediator.Send(new FallbackRequest(3)), Is.EqualTo(6));
        await mediator.Send(new FallbackCommand());

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new FallbackStreamRequest(3)))
        {
            items.Add(item);
        }
        Assert.That(items, Is.EqualTo(new[] { 0, 1, 2 }));
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

    [Test]
    public async Task CompositeStreamDispatcher_ShouldUseDispatcherThatKnowsTheRequestType()
    {
        var composite = new CompositeStreamDispatcher([new NoCaseStreamDispatcher(), new KnownStreamDispatcher()]);

        Assert.That(composite.CanDispatch(typeof(FallbackStreamRequest)), Is.True);

        var items = new List<int>();
        await foreach (var item in composite.DispatchStream(new FallbackStreamRequest(2), new FallbackStreamHandler(), CancellationToken.None))
        {
            items.Add(item);
        }
        Assert.That(items, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void CompositeStreamDispatcher_WhenNoDispatcherKnowsTheType_ShouldThrow()
    {
        var composite = new CompositeStreamDispatcher([new NoCaseStreamDispatcher()]);

        Assert.That(composite.CanDispatch(typeof(FallbackStreamRequest)), Is.False);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            composite.DispatchStream(new FallbackStreamRequest(1), new FallbackStreamHandler(), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("No stream dispatcher could handle request type"));
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

/// <summary>A dispatcher that knows no request types, like a generated dispatcher from another assembly.</summary>
public class NoCaseRequestDispatcher : IRequestDispatcher
{
    public bool CanDispatch(Type requestType) => false;

    public Task<TResponse> Dispatch<TResponse>(IRequest<TResponse> request, object handler, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}");

    public Task Dispatch(IRequest request, object handler, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}");
}

public class NoCaseStreamDispatcher : IStreamDispatcher
{
    public bool CanDispatch(Type requestType) => false;

    public IAsyncEnumerable<TResponse> DispatchStream<TResponse>(IStreamRequest<TResponse> request, object handler, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"No stream dispatcher case for request type {request.GetType().FullName}");
}

public class KnownStreamDispatcher : IStreamDispatcher
{
    public bool CanDispatch(Type requestType) => requestType == typeof(FallbackStreamRequest);

    public IAsyncEnumerable<TResponse> DispatchStream<TResponse>(IStreamRequest<TResponse> request, object handler, CancellationToken cancellationToken) =>
        (IAsyncEnumerable<TResponse>)((FallbackStreamHandler)handler).Handle((FallbackStreamRequest)request, cancellationToken);
}
