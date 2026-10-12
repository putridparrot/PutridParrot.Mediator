namespace PutridParrot.Mediator;

/// <summary>
/// Validates a request before its handler runs. Used by <see cref="ValidationBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The request (or command) type to validate.</typeparam>
/// <remarks>
/// Implementations are registered automatically by <c>AddMediator(assemblies)</c>.
/// Several validators can be registered for the same request type; all of them run.
/// </remarks>
/// <example>
/// <code>
/// public class CreateUserValidator : IRequestValidator&lt;CreateUserCommand&gt;
/// {
///     public Task&lt;IReadOnlyList&lt;ValidationFailure&gt;&gt; ValidateAsync(CreateUserCommand request, CancellationToken cancellationToken)
///     {
///         var failures = new List&lt;ValidationFailure&gt;();
///         if (string.IsNullOrWhiteSpace(request.Name))
///             failures.Add(new ValidationFailure(nameof(request.Name), "Name is required"));
///         return Task.FromResult&lt;IReadOnlyList&lt;ValidationFailure&gt;&gt;(failures);
///     }
/// }
/// </code>
/// </example>
public interface IRequestValidator<in TRequest>
{
    /// <summary>
    /// Validates the request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation failures; empty when the request is valid.</returns>
    Task<IReadOnlyList<ValidationFailure>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A single validation failure.
/// </summary>
/// <param name="PropertyName">The name of the invalid property, or an empty string for the request as a whole.</param>
/// <param name="ErrorMessage">A description of the failure.</param>
public sealed record ValidationFailure(string PropertyName, string ErrorMessage);

/// <summary>
/// Thrown by <see cref="ValidationBehavior{TRequest,TResponse}"/> when a request fails validation.
/// </summary>
public sealed class RequestValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the RequestValidationException class.
    /// </summary>
    /// <param name="requestType">The type of the request that failed validation.</param>
    /// <param name="failures">The validation failures.</param>
    public RequestValidationException(Type requestType, IReadOnlyList<ValidationFailure> failures)
        : base($"Validation failed for {requestType.Name}: " +
               string.Join("; ", failures.Select(f => string.IsNullOrEmpty(f.PropertyName) ? f.ErrorMessage : $"{f.PropertyName}: {f.ErrorMessage}")))
    {
        RequestType = requestType;
        Failures = failures;
    }

    /// <summary>
    /// The type of the request that failed validation.
    /// </summary>
    public Type RequestType { get; }

    /// <summary>
    /// The validation failures.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }
}

/// <summary>
/// Pipeline behavior that runs every registered <see cref="IRequestValidator{TRequest}"/> before the handler,
/// throwing <see cref="RequestValidationException"/> with all failures if any validator reports one.
/// </summary>
/// <typeparam name="TRequest">The type of request.</typeparam>
/// <typeparam name="TResponse">The type of response, or <see cref="Unit"/> for commands.</typeparam>
/// <example>
/// <code>
/// services.AddMediator(cfg =>
/// {
///     cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
///     cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;));
/// });
/// </code>
/// </example>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the ValidationBehavior class.
    /// </summary>
    /// <param name="validators">The validators registered for the request type.</param>
    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    /// <summary>
    /// Validates the request, then calls the next behavior or handler if it is valid.
    /// </summary>
    /// <exception cref="RequestValidationException">One or more validators reported a failure.</exception>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        List<ValidationFailure>? failures = null;

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (result.Count > 0)
            {
                (failures ??= []).AddRange(result);
            }
        }

        if (failures != null)
        {
            throw new RequestValidationException(typeof(TRequest), failures);
        }

        return await next();
    }
}
