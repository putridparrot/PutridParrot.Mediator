using System.Diagnostics;

namespace PutridParrot.Mediator.Example.Behaviors;

/// <summary>
/// Example pipeline behavior that measures request execution time.
/// Logs a warning if execution takes longer than a specified threshold.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response being returned.</typeparam>
public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<string> _logs;
    private readonly long _thresholdMs;

    /// <summary>
    /// Initializes a new instance of the PerformanceBehavior class.
    /// </summary>
    /// <param name="logs">A shared list to collect log messages.</param>
    /// <param name="thresholdMs">The threshold in milliseconds. Requests taking longer will be logged.</param>
    public PerformanceBehavior(List<string> logs, long thresholdMs = 500)
    {
        _logs = logs;
        _thresholdMs = thresholdMs;
    }

    /// <summary>
    /// Handles the request by measuring execution time and logging if it exceeds the threshold.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        
        var response = await next();
        
        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;
        
        if (elapsed > _thresholdMs)
        {
            _logs.Add($"[PerformanceBehavior] WARNING: {requestName} took {elapsed}ms (threshold: {_thresholdMs}ms)");
        }
        else
        {
            _logs.Add($"[PerformanceBehavior] {requestName} completed in {elapsed}ms");
        }
        
        return response;
    }
}
