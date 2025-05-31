using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class IntegrationTests
{
    [Test]
    public async Task EndToEnd_SimpleQuery_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.Send(new GetUserQuery(1));

        // Assert
        Assert.That(result.Id, Is.EqualTo(1));
        Assert.That(result.Name, Is.EqualTo("User 1"));
    }

    [Test]
    public async Task EndToEnd_SimpleCommand_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => 
            await mediator.Send(new CreateUserCommand("John", "john@example.com")));
    }

    [Test]
    public async Task EndToEnd_WithDependencyInjection_ShouldInjectDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IUserRepository, InMemoryUserRepository>();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new SaveUserCommand(5, "Alice"));
        var result = await mediator.Send(new GetSavedUserQuery(5));

        // Assert
        Assert.That(result.Id, Is.EqualTo(5));
        Assert.That(result.Name, Is.EqualTo("Alice"));
    }

    [Test]
    public async Task EndToEnd_MultipleSequentialRequests_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        var result1 = await mediator.Send(new CalculateQuery(5, 3, "add"));
        var result2 = await mediator.Send(new CalculateQuery(10, 2, "multiply"));
        var result3 = await mediator.Send(new CalculateQuery(20, 5, "divide"));

        // Assert
        Assert.That(result1, Is.EqualTo(8));
        Assert.That(result2, Is.EqualTo(20));
        Assert.That(result3, Is.EqualTo(4));
    }

    [Test]
    public async Task EndToEnd_ConcurrentRequests_ShouldHandleCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act
        var tasks = Enumerable.Range(1, 10)
            .Select(i => mediator.Send(new GetUserQuery(i)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.That(results, Has.Length.EqualTo(10));
        for (int i = 0; i < 10; i++)
        {
            Assert.That(results[i].Id, Is.EqualTo(i + 1));
            Assert.That(results[i].Name, Is.EqualTo($"User {i + 1}"));
        }
    }

    [Test]
    public async Task EndToEnd_HandlerWithException_ShouldPropagateException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Act & Assert - when using reflection path, exception is wrapped
        var ex = Assert.ThrowsAsync<TargetInvocationException>(async () => 
            await mediator.Send(new FailingQuery()));
        
        Assert.That(ex!.InnerException, Is.InstanceOf<InvalidOperationException>());
        Assert.That(ex.InnerException!.Message, Is.EqualTo("This query always fails"));
    }

    [Test]
    public async Task EndToEnd_WithRecordTypes_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);
        
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        var request = new RecordQuery("Test", 42);

        // Act
        var result = await mediator.Send(request);

        // Assert
        Assert.That(result.Value, Is.EqualTo("Test"));
        Assert.That(result.Count, Is.EqualTo(42));
    }
}

// Test types and handlers
public record GetUserQuery(int Id) : IRequest<UserDto>;

public record UserDto(int Id, string Name);

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new UserDto(request.Id, $"User {request.Id}"));
    }
}

public record CreateUserCommand(string Name, string Email) : IRequest;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand>
{
    public Task Handle(CreateUserCommand request, CancellationToken cancellationToken = default)
    {
        // Simulate creating user
        return Task.CompletedTask;
    }
}

public interface IUserRepository
{
    Task SaveUser(int id, string name);
    Task<UserDto?> GetUser(int id);
}

public class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<int, UserDto> _users = new();

    public Task SaveUser(int id, string name)
    {
        _users[id] = new UserDto(id, name);
        return Task.CompletedTask;
    }

    public Task<UserDto?> GetUser(int id)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }
}

public record SaveUserCommand(int Id, string Name) : IRequest;

public class SaveUserCommandHandler : IRequestHandler<SaveUserCommand>
{
    private readonly IUserRepository _repository;

    public SaveUserCommandHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(SaveUserCommand request, CancellationToken cancellationToken = default)
    {
        await _repository.SaveUser(request.Id, request.Name);
    }
}

public record GetSavedUserQuery(int Id) : IRequest<UserDto>;

public class GetSavedUserQueryHandler : IRequestHandler<GetSavedUserQuery, UserDto>
{
    private readonly IUserRepository _repository;

    public GetSavedUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<UserDto> Handle(GetSavedUserQuery request, CancellationToken cancellationToken = default)
    {
        var user = await _repository.GetUser(request.Id);
        return user ?? new UserDto(request.Id, "Unknown");
    }
}

public record CalculateQuery(int A, int B, string Operation) : IRequest<int>;

public class CalculateQueryHandler : IRequestHandler<CalculateQuery, int>
{
    public Task<int> Handle(CalculateQuery request, CancellationToken cancellationToken = default)
    {
        var result = request.Operation.ToLower() switch
        {
            "add" => request.A + request.B,
            "subtract" => request.A - request.B,
            "multiply" => request.A * request.B,
            "divide" => request.A / request.B,
            _ => throw new ArgumentException($"Unknown operation: {request.Operation}")
        };
        
        return Task.FromResult(result);
    }
}

public record FailingQuery : IRequest<string>;

public class FailingQueryHandler : IRequestHandler<FailingQuery, string>
{
    public Task<string> Handle(FailingQuery request, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("This query always fails");
    }
}

public record RecordQuery(string Value, int Count) : IRequest<RecordResult>;

public record RecordResult(string Value, int Count);

public class RecordQueryHandler : IRequestHandler<RecordQuery, RecordResult>
{
    public Task<RecordResult> Handle(RecordQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RecordResult(request.Value, request.Count));
    }
}
