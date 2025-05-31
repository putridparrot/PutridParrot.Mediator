namespace PutridParrot.Mediator.Example.Behaviors;

/// <summary>
/// Example pipeline behavior that logs request execution.
/// Demonstrates how to implement cross-cutting concerns using IPipelineBehavior.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response being returned.</typeparam>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<string> _logs;

    /// <summary>
    /// Initializes a new instance of the LoggingBehavior class.
    /// </summary>
    /// <param name="logs">A shared list to collect log messages for demonstration purposes.</param>
    public LoggingBehavior(List<string> logs)
    {
        _logs = logs;
    }

    /// <summary>
    /// Handles the request by logging before and after the next handler executes.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        
        _logs.Add($"[LoggingBehavior] Handling {requestName}");
        _logs.Add($"[LoggingBehavior] Request details: {request}");
        
        var response = await next();
        
        _logs.Add($"[LoggingBehavior] Handled {requestName} successfully");
        _logs.Add($"[LoggingBehavior] Response: {response}");
        
        return response;
    }
}
