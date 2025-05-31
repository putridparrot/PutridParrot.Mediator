using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class RequestHandlerInterfaceTests
{
    [Test]
    public async Task IRequestHandler_WithResponse_ShouldHandleRequest()
    {
        // Arrange
        var handler = new HandlerTestQueryHandler();
        var request = new HandlerTestQueryRequest("test");

        // Act
        var result = await handler.Handle(request);

        // Assert
        Assert.That(result, Is.EqualTo("test-response"));
    }

    [Test]
    public async Task IRequestHandler_WithoutResponse_ShouldHandleRequest()
    {
        // Arrange
        var handler = new HandlerTestCommandHandler();
        var request = new HandlerTestCommandRequest();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await handler.Handle(request));
    }

    [Test]
    public async Task IRequestHandler_ShouldSupportCancellationToken()
    {
        // Arrange
        var handler = new CancellableQueryHandler();
        var request = new CancellableQuery();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var ex = Assert.ThrowsAsync<TaskCanceledException>(async () => 
            await handler.Handle(request, cts.Token));
        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public async Task IRequestHandler_WithComplexReturnType_ShouldWork()
    {
        // Arrange
        var handler = new ComplexQueryHandler();
        var request = new ComplexQuery(1, "Test");

        // Act
        var result = await handler.Handle(request);

        // Assert
        Assert.That(result.Id, Is.EqualTo(1));
        Assert.That(result.Name, Is.EqualTo("Test"));
        Assert.That(result.Items, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task IRequestHandler_WithAsyncOperation_ShouldWork()
    {
        // Arrange
        var handler = new AsyncQueryHandler();
        var request = new AsyncQuery(50);

        // Act
        var result = await handler.Handle(request);

        // Assert
        Assert.That(result, Is.EqualTo(50));
    }

    [Test]
    public async Task IRequestHandler_WithGenericConstraints_ShouldWork()
    {
        // Arrange
        var handler = new ListQueryHandler();
        var request = new ListQuery();

        // Act
        var result = await handler.Handle(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task IRequestHandler_MultipleHandlersForDifferentRequests_ShouldWork()
    {
        // Arrange
        var handler = new MultiRequestHandler();
        var request1 = new MultiRequest1("test1");
        var request2 = new MultiRequest2(42);

        // Act
        var result1 = await handler.Handle(request1);
        var result2 = await handler.Handle(request2);

        // Assert
        Assert.That(result1, Is.EqualTo("test1-handled"));
        Assert.That(result2, Is.EqualTo(84));
    }
}

// Test types
public record HandlerTestQueryRequest(string Input) : IRequest<string>;

public class HandlerTestQueryHandler : IRequestHandler<HandlerTestQueryRequest, string>
{
    public Task<string> Handle(HandlerTestQueryRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{request.Input}-response");
    }
}

public record HandlerTestCommandRequest : IRequest;

public class HandlerTestCommandHandler : IRequestHandler<HandlerTestCommandRequest>
{
    public Task Handle(HandlerTestCommandRequest request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public record CancellableQuery : IRequest<bool>;

public class CancellableQueryHandler : IRequestHandler<CancellableQuery, bool>
{
    public async Task<bool> Handle(CancellableQuery request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        return true;
    }
}

public record ComplexQuery(int Id, string Name) : IRequest<ComplexResult>;

public record ComplexResult(int Id, string Name, List<string> Items);

public class ComplexQueryHandler : IRequestHandler<ComplexQuery, ComplexResult>
{
    public Task<ComplexResult> Handle(ComplexQuery request, CancellationToken cancellationToken = default)
    {
        var result = new ComplexResult(
            request.Id,
            request.Name,
            new List<string> { "Item1", "Item2", "Item3" }
        );
        return Task.FromResult(result);
    }
}

public record AsyncQuery(int Delay) : IRequest<int>;

public class AsyncQueryHandler : IRequestHandler<AsyncQuery, int>
{
    public async Task<int> Handle(AsyncQuery request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(request.Delay, cancellationToken);
        return request.Delay;
    }
}

public record ListQuery : IRequest<List<string>>;

public class ListQueryHandler : IRequestHandler<ListQuery, List<string>>
{
    public Task<List<string>> Handle(ListQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string> { "A", "B", "C" });
    }
}

public record MultiRequest1(string Value) : IRequest<string>;
public record MultiRequest2(int Value) : IRequest<int>;

public class MultiRequestHandler : 
    IRequestHandler<MultiRequest1, string>,
    IRequestHandler<MultiRequest2, int>
{
    public Task<string> Handle(MultiRequest1 request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{request.Value}-handled");
    }

    public Task<int> Handle(MultiRequest2 request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}
