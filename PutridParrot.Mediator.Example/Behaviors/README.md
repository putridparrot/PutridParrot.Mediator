# Pipeline Behaviors

This directory contains example implementations of `IPipelineBehavior<TRequest, TResponse>` that demonstrate how to implement cross-cutting concerns in the PutridParrot.Mediator library.

## What are Pipeline Behaviors?

Pipeline behaviors are a powerful way to add cross-cutting concerns to your request/response pipeline. They execute before and after your request handlers, allowing you to:

- Log requests and responses
- Validate requests before processing
- Measure performance and timing
- Handle exceptions
- Add caching
- Manage transactions
- And much more!

## Architecture

Behaviors are executed in a pipeline pattern:

```
Request ? Behavior 1 ? Behavior 2 ? Handler ? Behavior 2 ? Behavior 1 ? Response
          [Before]     [Before]      [Execute]  [After]      [After]
```

Each behavior can:
1. Execute code **before** calling the next behavior/handler
2. Call `next()` to continue the pipeline
3. Execute code **after** the next behavior/handler completes
4. Transform or validate the request/response
5. Short-circuit the pipeline by throwing exceptions or not calling `next()`

## Examples Included

### 1. LoggingBehavior

Logs request and response information for debugging and auditing purposes.

**Use Case:** Track all requests flowing through your application.

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

**Output:**
```
[LoggingBehavior] Handling GetUserRequest
[LoggingBehavior] Request details: GetUserRequest(Id=1)
[LoggingBehavior] Handled GetUserRequest successfully
[LoggingBehavior] Response: UserDto(Id=1, Name=User 1)
```

### 2. PerformanceBehavior

Measures execution time and logs warnings for slow requests.

**Use Case:** Identify performance bottlenecks in your handlers.

```csharp
services.AddTransient(sp => new PerformanceBehavior<TRequest, TResponse>(
    logs, 
    thresholdMs: 500)); // Warn if request takes > 500ms
```

**Output:**
```
[PerformanceBehavior] GetUserRequest completed in 45ms
[PerformanceBehavior] WARNING: SlowQueryRequest took 1250ms (threshold: 500ms)
```

### 3. ValidationBehavior

Validates requests before they reach the handler, short-circuiting on validation failure.

**Use Case:** Ensure all requests meet business rules before processing.

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
```

**Behavior:**
- If validation passes ? continues to handler
- If validation fails ? throws `ValidationException` (handler never executes)

## How to Register Pipeline Behaviors

### Register for All Requests

Use open generics to register a behavior for all request types:

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### Register for Specific Request Types

Register a behavior for a specific request/response pair:

```csharp
services.AddTransient<IPipelineBehavior<CreateUserCommand, UserDto>>(
    sp => new ValidationBehavior<CreateUserCommand, UserDto>(
        sp.GetRequiredService<List<string>>(),
        sp.GetRequiredService<IEnumerable<IValidator<CreateUserCommand>>>()));
```

### Execution Order

Behaviors execute in the order they are registered:

```csharp
// This order:
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));      // 1
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));   // 2
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));  // 3

// Results in this pipeline:
// Request ? Logging ? Validation ? Performance ? Handler ? Performance ? Validation ? Logging ? Response
```

## Creating Your Own Behavior

Implement the `IPipelineBehavior<TRequest, TResponse>` interface:

```csharp
public class MyCustomBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        // Code BEFORE handler executes
        Console.WriteLine("Before handler");
        
        // Call the next behavior/handler in the pipeline
        var response = await next();
        
        // Code AFTER handler executes
        Console.WriteLine("After handler");
        
        return response;
    }
}
```

### Common Patterns

#### 1. Logging Pattern
```csharp
public async Task<TResponse> Handle(...)
{
    _logger.LogInformation($"Handling {typeof(TRequest).Name}");
    var response = await next();
    _logger.LogInformation($"Handled {typeof(TRequest).Name}");
    return response;
}
```

#### 2. Validation Pattern (Short-Circuit)
```csharp
public async Task<TResponse> Handle(...)
{
    var validationResult = ValidateRequest(request);
    if (!validationResult.IsValid)
    {
        throw new ValidationException(validationResult.Errors);
        // Note: next() is NOT called - pipeline is short-circuited
    }
    
    return await next();
}
```

#### 3. Try-Catch Pattern
```csharp
public async Task<TResponse> Handle(...)
{
    try
    {
        return await next();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error handling {typeof(TRequest).Name}");
        throw; // or handle gracefully
    }
}
```

#### 4. Transform Pattern
```csharp
public async Task<TResponse> Handle(...)
{
    var response = await next();
    
    // Transform the response
    return TransformResponse(response);
}
```

#### 5. Caching Pattern
```csharp
public async Task<TResponse> Handle(...)
{
    var cacheKey = GenerateCacheKey(request);
    
    if (_cache.TryGet(cacheKey, out TResponse cachedResponse))
    {
        return cachedResponse;
    }
    
    var response = await next();
    _cache.Set(cacheKey, response);
    
    return response;
}
```

## Testing Pipeline Behaviors

See `PutridParrot.Mediator.Tests/PipelineBehaviors/PipelineBehaviorTests.cs` for comprehensive test examples:

- ? Testing single behaviors
- ? Testing multiple behaviors in sequence
- ? Testing short-circuit scenarios (validation failures)
- ? Testing response transformation
- ? Testing with cancellation tokens

## Best Practices

1. **Keep behaviors focused** - Each behavior should do one thing well
2. **Order matters** - Think about the order behaviors should execute
3. **Performance** - Behaviors execute on every request, keep them fast
4. **Async all the way** - Always `await next()` to avoid blocking
5. **Handle cancellation** - Pass and respect `CancellationToken`
6. **Document behavior** - Use XML comments to explain what each behavior does

## Common Use Cases

| Behavior Type | Purpose | When to Use |
|--------------|---------|-------------|
| **Logging** | Track request/response | Debugging, auditing, monitoring |
| **Validation** | Validate business rules | Input validation, authorization |
| **Performance** | Measure execution time | Performance monitoring, SLA tracking |
| **Caching** | Cache responses | Read-heavy operations |
| **Transaction** | Manage database transactions | Write operations, consistency |
| **Exception Handling** | Centralized error handling | Error logging, user-friendly errors |
| **Authorization** | Check permissions | Security, access control |
| **Retry** | Retry failed operations | Transient failures, external APIs |

## Running the Example

```bash
cd PutridParrot.Mediator.Example
dotnet run
```

You'll see output demonstrating how pipeline behaviors intercept and enhance request processing.

## Learn More

- [Mediator Pattern](https://en.wikipedia.org/wiki/Mediator_pattern)
- [Decorator Pattern](https://en.wikipedia.org/wiki/Decorator_pattern) (similar concept)
- [MediatR Documentation](https://github.com/jbogard/MediatR) (similar library with behaviors)
