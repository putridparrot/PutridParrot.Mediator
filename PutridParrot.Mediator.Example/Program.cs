using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

// Setup
var services = new ServiceCollection();
services.AddMediator(typeof(Program).Assembly);

var serviceProvider = services.BuildServiceProvider();
var mediator = serviceProvider.GetRequiredService<IMediator>();

// Example 1: Request with Response
Console.WriteLine("=== Example 1: Request with Response ===");
var getUserRequest = new GetUserByIdRequest(1);
var user = await mediator.Send(getUserRequest);
Console.WriteLine($"User: {user.Id} - {user.Name}");

// Example 2: Request without Response (Command)
Console.WriteLine("\n=== Example 2: Command (no response) ===");
var createUserRequest = new CreateUserCommand("John Doe", "john@example.com");
await mediator.Send(createUserRequest);

// Example 3: Another query
Console.WriteLine("\n=== Example 3: Calculate Sum ===");
var calculateRequest = new CalculateSumRequest(5, 10);
var sum = await mediator.Send(calculateRequest);
Console.WriteLine($"Sum: {sum}");

Console.WriteLine("\nDone!");

// ===== Requests =====

/// <summary>
/// Request to get a user by ID.
/// </summary>
public record GetUserByIdRequest(int Id) : IRequest<UserDto>
{
    public override string ToString() => $"GetUserByIdRequest(Id={Id})";
}

/// <summary>
/// User data transfer object.
/// </summary>
public record UserDto(int Id, string Name)
{
    public override string ToString() => $"UserDto(Id={Id}, Name={Name})";
}

/// <summary>
/// Command to create a new user.
/// </summary>
public record CreateUserCommand(string Name, string Email) : IRequest
{
    public override string ToString() => $"CreateUserCommand(Name={Name}, Email={Email})";
}

/// <summary>
/// Request to calculate sum of two numbers.
/// </summary>
public record CalculateSumRequest(int A, int B) : IRequest<int>
{
    public override string ToString() => $"CalculateSumRequest(A={A}, B={B})";
}

// ===== Handlers =====

public class GetUserByIdHandler : IRequestHandler<GetUserByIdRequest, UserDto>
{
    public Task<UserDto> Handle(GetUserByIdRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handling GetUserByIdRequest for ID: {request.Id}");
        return Task.FromResult(new UserDto(request.Id, $"User {request.Id}"));
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand>
{
    public Task Handle(CreateUserCommand request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Creating user: {request.Name} ({request.Email})");
        // In real app, this would save to database
        return Task.CompletedTask;
    }
}

public class CalculateSumHandler : IRequestHandler<CalculateSumRequest, int>
{
    public Task<int> Handle(CalculateSumRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Calculating sum of {request.A} + {request.B}");
        return Task.FromResult(request.A + request.B);
    }
}

