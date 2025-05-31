using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class MediatorWithDispatcherTests
{
    [Test]
    public async Task Send_WithDispatcher_ShouldUseDispatcherInsteadOfReflection()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDispatcher = new MockRequestDispatcher();
        
        services.AddSingleton<IRequestDispatcher>(mockDispatcher);
        services.AddSingleton<IMediator>(sp => new Mediator(sp, mockDispatcher));
        services.AddTransient<IRequestHandler<DispatcherTestRequest, int>, DispatcherTestRequestHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new DispatcherTestRequest(10);

        // Act
        var result = await mediator.Send(request);

        // Assert
        Assert.That(result, Is.EqualTo(100)); // Dispatcher multiplies by 10
        Assert.That(mockDispatcher.DispatchWithResponseCalled, Is.True);
    }

    [Test]
    public async Task Send_CommandWithDispatcher_ShouldUseDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockDispatcher = new MockRequestDispatcher();
        
        services.AddSingleton<IRequestDispatcher>(mockDispatcher);
        services.AddSingleton<IMediator>(sp => new Mediator(sp, mockDispatcher));
        services.AddTransient<IRequestHandler<DispatcherTestCommand>, DispatcherTestCommandHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new DispatcherTestCommand();

        // Act
        await mediator.Send(request);

        // Assert
        Assert.That(mockDispatcher.DispatchWithoutResponseCalled, Is.True);
    }

    [Test]
    public async Task Send_WithoutDispatcher_ShouldUseReflection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMediator>(sp => new Mediator(sp, null));
        services.AddTransient<IRequestHandler<DispatcherTestRequest, int>, DispatcherTestRequestHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new DispatcherTestRequest(5);

        // Act
        var result = await mediator.Send(request);

        // Assert
        Assert.That(result, Is.EqualTo(10)); // Handler doubles the value
    }

    [Test]
    public void Mediator_Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Mediator(null!));
    }

    [Test]
    public void Mediator_Constructor_WithValidServiceProvider_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        Assert.DoesNotThrow(() => new Mediator(serviceProvider));
    }

    [Test]
    public void Mediator_Constructor_WithDispatcher_ShouldAcceptNullDispatcher()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        Assert.DoesNotThrow(() => new Mediator(serviceProvider, null));
    }
}

// Mock Dispatcher for testing
public class MockRequestDispatcher : IRequestDispatcher
{
    public bool DispatchWithResponseCalled { get; private set; }
    public bool DispatchWithoutResponseCalled { get; private set; }

    public Task<TResponse> Dispatch<TResponse>(IRequest<TResponse> request, object handler, CancellationToken cancellationToken)
    {
        DispatchWithResponseCalled = true;
        
        if (request is DispatcherTestRequest dtr && handler is IRequestHandler<DispatcherTestRequest, int> h)
        {
            // Simulate different behavior than handler to verify dispatcher was used
            return Task.FromResult((TResponse)(object)(dtr.Value * 10));
        }
        
        throw new NotSupportedException("Handler type not supported in mock");
    }

    public Task Dispatch(IRequest request, object handler, CancellationToken cancellationToken)
    {
        DispatchWithoutResponseCalled = true;
        return Task.CompletedTask;
    }
}

// Test types
public record DispatcherTestRequest(int Value) : IRequest<int>;

public class DispatcherTestRequestHandler : IRequestHandler<DispatcherTestRequest, int>
{
    public Task<int> Handle(DispatcherTestRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}

public record DispatcherTestCommand : IRequest;

public class DispatcherTestCommandHandler : IRequestHandler<DispatcherTestCommand>
{
    public Task Handle(DispatcherTestCommand request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
