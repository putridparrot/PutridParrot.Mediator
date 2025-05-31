using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;
using System.Reflection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddMediator_WithoutAssemblies_ShouldRegisterMediator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMediator();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var mediator = serviceProvider.GetService<IMediator>();
        Assert.That(mediator, Is.Not.Null);
        Assert.That(mediator, Is.InstanceOf<Mediator>());
    }

    [Test]
    public void AddMediator_WithAssemblies_ShouldRegisterMediatorAndHandlers()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMediator(typeof(ServiceCollectionExtensionsTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var mediator = serviceProvider.GetService<IMediator>();
        Assert.That(mediator, Is.Not.Null);
        
        // Check if handler is registered
        var handler = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();
        Assert.That(handler, Is.Not.Null);
    }

    [Test]
    public void AddMediator_ShouldRegisterMediatorAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mediator1 = serviceProvider.GetService<IMediator>();
        var mediator2 = serviceProvider.GetService<IMediator>();

        // Assert
        Assert.That(mediator1, Is.SameAs(mediator2));
    }

    [Test]
    public void AddRequestHandlers_ShouldRegisterAllHandlersFromAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRequestHandlers(typeof(ServiceCollectionExtensionsTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var handler1 = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();
        var handler2 = serviceProvider.GetService<IRequestHandler<TestExtensionCommandRequest>>();
        
        Assert.That(handler1, Is.Not.Null);
        Assert.That(handler2, Is.Not.Null);
    }

    [Test]
    public void AddRequestHandlers_ShouldRegisterHandlersAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRequestHandlers(typeof(ServiceCollectionExtensionsTests).Assembly);

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var handler1 = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();
        var handler2 = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();

        // Assert
        Assert.That(handler1, Is.Not.SameAs(handler2));
    }

    [Test]
    public void AddRequestHandlers_WithMultipleAssemblies_ShouldRegisterHandlersFromAll()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly1 = typeof(ServiceCollectionExtensionsTests).Assembly;
        var assembly2 = typeof(IMediator).Assembly;

        // Act
        services.AddRequestHandlers(assembly1, assembly2);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var handler = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();
        Assert.That(handler, Is.Not.Null);
    }

    [Test]
    public void AddRequestHandlers_ShouldNotRegisterAbstractClasses()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddRequestHandlers(typeof(ServiceCollectionExtensionsTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var handler = serviceProvider.GetService<IRequestHandler<AbstractHandlerRequest, string>>();
        Assert.That(handler, Is.Null);
    }

    [Test]
    public void AddMediator_WithMultipleAssemblies_ShouldRegisterAll()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly1 = typeof(ServiceCollectionExtensionsTests).Assembly;
        var assembly2 = typeof(IMediator).Assembly;

        // Act
        services.AddMediator(assembly1, assembly2);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var mediator = serviceProvider.GetService<IMediator>();
        var handler = serviceProvider.GetService<IRequestHandler<TestExtensionRequest, string>>();
        
        Assert.That(mediator, Is.Not.Null);
        Assert.That(handler, Is.Not.Null);
    }
}

// Test types for ServiceCollectionExtensionsTests
public record TestExtensionRequest(string Value) : IRequest<string>;

public class TestExtensionRequestHandler : IRequestHandler<TestExtensionRequest, string>
{
    public Task<string> Handle(TestExtensionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Handled: {request.Value}");
    }
}

public record TestExtensionCommandRequest : IRequest;

public class TestExtensionCommandRequestHandler : IRequestHandler<TestExtensionCommandRequest>
{
    public Task Handle(TestExtensionCommandRequest request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public record AbstractHandlerRequest(string Value) : IRequest<string>;

public abstract class AbstractHandlerBase : IRequestHandler<AbstractHandlerRequest, string>
{
    public abstract Task<string> Handle(AbstractHandlerRequest request, CancellationToken cancellationToken = default);
}
