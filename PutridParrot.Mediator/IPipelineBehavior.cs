namespace PutridParrot.Mediator;

/// <summary>
/// Pipeline behavior for cross-cutting concerns
/// Executes before and after handler execution
/// </summary>
/// <typeparam name="TRequest">The type of request</typeparam>
/// <typeparam name="TResponse">The type of response</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> 
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handle the request and optionally call the next behavior in the pipeline
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="next">Delegate to call the next behavior or handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from the handler</returns>
    Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken);
}

/// <summary>
/// Delegate that represents the next handler in the pipeline
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// Example behaviors (for reference - users can implement these)

/// <summary>
/// Example: Logging behavior that logs before and after handler execution
/// </summary>
/*
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }
    
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Handling {typeof(TRequest).Name}");
        var response = await next();
        _logger.LogInformation($"Handled {typeof(TRequest).Name}");
        return response;
    }
}
*/

/// <summary>
/// Example: Validation behavior using FluentValidation
/// </summary>
/*
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }
    
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = _validators
                .Select(v => v.Validate(context))
                .SelectMany(result => result.Errors)
                .Where(f => f != null)
                .ToList();
                
            if (failures.Count != 0)
                throw new ValidationException(failures);
        }
        
        return await next();
    }
}
*/

/// <summary>
/// Example: Performance logging behavior
/// </summary>
/*
public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly Stopwatch _timer;
    
    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
        _timer = new Stopwatch();
    }
    
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        _timer.Start();
        var response = await next();
        _timer.Stop();
        
        var elapsed = _timer.ElapsedMilliseconds;
        if (elapsed > 500) // Log if slower than 500ms
        {
            _logger.LogWarning($"{typeof(TRequest).Name} took {elapsed}ms");
        }
        
        return response;
    }
}
*/
