using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class MediatorTests
{
    private ServiceProvider _serviceProvider = null!;
    private IMediator _mediator = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(MediatorTests).Assembly);
        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    [Test]
    public async Task Send_WithRequestReturningResponse_ShouldInvokeHandlerAndReturnResponse()
    {
        // Arrange
        var request = new TestRequest(42);

        // Act
        var result = await _mediator.Send(request);

        // Assert
        Assert.That(result, Is.EqualTo(84));
    }

    [Test]
    public async Task Send_WithRequestWithoutResponse_ShouldInvokeHandler()
    {
        // Arrange
        var request = new TestCommandRequest();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await _mediator.Send(request));
    }

    [Test]
    public void Send_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        TestRequest request = null!;

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () => await _mediator.Send(request));
    }

    [Test]
    public void Send_WithNullCommandRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        TestCommandRequest request = null!;

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () => await _mediator.Send(request));
    }

    [Test]
    public void Send_WithNoHandlerRegistered_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new UnhandledRequest();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await _mediator.Send(request));
        Assert.That(ex!.Message, Does.Contain("No handler registered for request type"));
    }

    [Test]
    public void Send_WithNoCommandHandlerRegistered_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new UnhandledCommandRequest();

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await _mediator.Send(request));
        Assert.That(ex!.Message, Does.Contain("No handler registered for request type"));
    }

    [Test]
    public async Task Send_WithCancellationToken_ShouldPassTokenToHandler()
    {
        // Arrange
        var request = new CancellableRequest();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - the handler's own exception propagates, not a reflection wrapper
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await _mediator.Send(request, cts.Token));
    }

    [Test]
    public async Task Send_WithStringResponse_ShouldReturnCorrectString()
    {
        // Arrange
        var request = new StringRequest("Hello");

        // Act
        var result = await _mediator.Send(request);

        // Assert
        Assert.That(result, Is.EqualTo("Hello World"));
    }

    [Test]
    public async Task Send_WithComplexObject_ShouldReturnComplexObject()
    {
        // Arrange
        var request = new ComplexObjectRequest(1, "Test");

        // Act
        var result = await _mediator.Send(request);

        // Assert
        Assert.That(result.Id, Is.EqualTo(1));
        Assert.That(result.Name, Is.EqualTo("Test"));
        Assert.That(result.Timestamp, Is.Not.EqualTo(default(DateTime)));
    }
}

// Test Requests and Handlers
public record TestRequest(int Value) : IRequest<int>;

public class TestRequestHandler : IRequestHandler<TestRequest, int>
{
    public Task<int> Handle(TestRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}

public record TestCommandRequest : IRequest
{
    public bool WasHandled { get; set; }
}

public class TestCommandRequestHandler : IRequestHandler<TestCommandRequest>
{
    public Task Handle(TestCommandRequest request, CancellationToken cancellationToken = default)
    {
        request.WasHandled = true;
        return Task.CompletedTask;
    }
}

public record UnhandledRequest : IRequest<string>;

public record UnhandledCommandRequest : IRequest;

public record CancellableRequest : IRequest<bool>;

public class CancellableRequestHandler : IRequestHandler<CancellableRequest, bool>
{
    public Task<bool> Handle(CancellableRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

public record StringRequest(string Input) : IRequest<string>;

public class StringRequestHandler : IRequestHandler<StringRequest, string>
{
    public Task<string> Handle(StringRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"{request.Input} World");
    }
}

public record ComplexObjectRequest(int Id, string Name) : IRequest<ComplexObjectResponse>;

public record ComplexObjectResponse(int Id, string Name, DateTime Timestamp);

public class ComplexObjectRequestHandler : IRequestHandler<ComplexObjectRequest, ComplexObjectResponse>
{
    public Task<ComplexObjectResponse> Handle(ComplexObjectRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ComplexObjectResponse(request.Id, request.Name, DateTime.UtcNow));
    }
}
