using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class CompositeRequestDispatcherTests
{
    [Test]
    public async Task Dispatch_WithMultipleDispatchers_ShouldTryEachUntilSuccess()
    {
        // Arrange
        var dispatcher1 = new MockDispatcher1();
        var dispatcher2 = new MockDispatcher2();
        var composite = new CompositeRequestDispatcher(new IRequestDispatcher[] { dispatcher1, dispatcher2 });
        
        var request = new Dispatcher2Request(42);
        var handler = new Dispatcher2RequestHandler();

        // Act
        var result = await composite.Dispatch(request, handler, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(84));
    }

    [Test]
    public async Task Dispatch_WithNoMatchingDispatcher_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var dispatcher1 = new MockDispatcher1();
        var dispatcher2 = new MockDispatcher2();
        var composite = new CompositeRequestDispatcher(new IRequestDispatcher[] { dispatcher1, dispatcher2 });
        
        var request = new UnknownRequest();
        var handler = new object();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await composite.Dispatch(request, handler, CancellationToken.None));
        
        Assert.That(ex!.Message, Does.Contain("No dispatcher could handle request type"));
        Assert.That(ex.Message, Does.Contain("Tried 2 dispatcher(s)"));
    }

    [Test]
    public async Task Dispatch_Command_WithMultipleDispatchers_ShouldWork()
    {
        // Arrange
        var dispatcher1 = new MockDispatcher1();
        var dispatcher2 = new MockDispatcher2();
        var composite = new CompositeRequestDispatcher(new IRequestDispatcher[] { dispatcher1, dispatcher2 });
        
        var request = new Dispatcher1Command();
        var handler = new Dispatcher1CommandHandler();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await composite.Dispatch((IRequest)request, handler, CancellationToken.None));
    }

    [Test]
    public void Constructor_WithEmptyDispatchers_ShouldNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => new CompositeRequestDispatcher(Array.Empty<IRequestDispatcher>()));
    }

    [Test]
    public async Task Dispatch_WithSingleDispatcher_ShouldWorkLikeNormal()
    {
        // Arrange
        var dispatcher = new MockDispatcher1();
        var composite = new CompositeRequestDispatcher(new IRequestDispatcher[] { dispatcher });
        
        var request = new Dispatcher1Request(10);
        var handler = new Dispatcher1RequestHandler();

        // Act
        var result = await composite.Dispatch(request, handler, CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(20));
    }
}

// Mock Dispatchers for testing
public class MockDispatcher1 : IRequestDispatcher
{
    public Task<TResponse> Dispatch<TResponse>(IRequest<TResponse> request, object handler, CancellationToken cancellationToken)
    {
        return request switch
        {
            Dispatcher1Request r => (Task<TResponse>)(object)((IRequestHandler<Dispatcher1Request, int>)handler).Handle(r, cancellationToken),
            _ => throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}")
        };
    }

    public Task Dispatch(IRequest request, object handler, CancellationToken cancellationToken)
    {
        return request switch
        {
            Dispatcher1Command r => ((IRequestHandler<Dispatcher1Command>)handler).Handle(r, cancellationToken),
            _ => throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}")
        };
    }
}

public class MockDispatcher2 : IRequestDispatcher
{
    public Task<TResponse> Dispatch<TResponse>(IRequest<TResponse> request, object handler, CancellationToken cancellationToken)
    {
        return request switch
        {
            Dispatcher2Request r => (Task<TResponse>)(object)((IRequestHandler<Dispatcher2Request, int>)handler).Handle(r, cancellationToken),
            _ => throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}")
        };
    }

    public Task Dispatch(IRequest request, object handler, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException($"No dispatcher case for request type {request.GetType().FullName}");
    }
}

// Test types for Dispatcher 1
public record Dispatcher1Request(int Value) : IRequest<int>;

public class Dispatcher1RequestHandler : IRequestHandler<Dispatcher1Request, int>
{
    public Task<int> Handle(Dispatcher1Request request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}

public record Dispatcher1Command : IRequest;

public class Dispatcher1CommandHandler : IRequestHandler<Dispatcher1Command>
{
    public Task Handle(Dispatcher1Command request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

// Test types for Dispatcher 2
public record Dispatcher2Request(int Value) : IRequest<int>;

public class Dispatcher2RequestHandler : IRequestHandler<Dispatcher2Request, int>
{
    public Task<int> Handle(Dispatcher2Request request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}

// Unknown type
public record UnknownRequest : IRequest<string>;
