using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator;

/// <summary>
/// Pipeline behavior that logs request execution details.
/// This behavior can be optionally registered to add logging to the mediator pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response being returned.</typeparam>
/// <remarks>
/// This behavior logs at Debug level for normal execution and Error level for failures.
/// It captures request name, handler execution time, and any exceptions that occur.
/// </remarks>
public class LoggingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingPipelineBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the LoggingPipelineBehavior class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the request by logging before and after handler execution.
    /// </summary>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">Delegate to call the next behavior or handler in the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the handler.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        
        _logger.LogDebug("Handling request: {RequestName}", requestName);
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await next();
            stopwatch.Stop();
            
            _logger.LogDebug(
                "Request {RequestName} completed in {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Request {RequestName} failed with exception: {ExceptionMessage}",
                requestName,
                ex.Message);
            throw;
        }
    }
}
