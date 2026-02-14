namespace PutridParrot.Mediator.Example.Behaviors;

/// <summary>
/// Example pipeline behavior that validates requests before they are handled.
/// Demonstrates how to short-circuit the pipeline by throwing exceptions.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response being returned.</typeparam>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<string> _logs;
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the ValidationBehavior class.
    /// </summary>
    /// <param name="logs">A shared list to collect log messages.</param>
    /// <param name="validators">Collection of validators to run against the request.</param>
    public ValidationBehavior(List<string> logs, IEnumerable<IValidator<TRequest>> validators)
    {
        _logs = logs;
        _validators = validators;
    }

    /// <summary>
    /// Handles the request by validating it first, then calling the next handler if validation passes.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logs.Add($"[ValidationBehavior] Validating {requestName}");
        
        var failures = new List<string>();
        
        foreach (var validator in _validators)
        {
            var result = validator.Validate(request);
            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }
        
        if (failures.Count > 0)
        {
            var errorMessage = string.Join("; ", failures);
            _logs.Add($"[ValidationBehavior] Validation failed: {errorMessage}");
            throw new ValidationException($"Validation failed for {requestName}: {errorMessage}");
        }
        
        _logs.Add($"[ValidationBehavior] Validation passed for {requestName}");
        
        return await next();
    }
}

/// <summary>
/// Simple validator interface for demonstration purposes.
/// </summary>
public interface IValidator<in T>
{
    ValidationResult Validate(T request);
}

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Exception thrown when request validation fails.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
