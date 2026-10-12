namespace PutridParrot.Mediator.Example.Behaviors;

/// <summary>
/// Simple timing behavior that works with automatic discovery.
/// No constructor dependencies required.
/// </summary>
public class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        
        Console.WriteLine($"[Timing] ??  Starting: {typeof(TRequest).Name}");
        
        var response = await next();
        
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        Console.WriteLine($"[Timing] ? Completed: {typeof(TRequest).Name} in {elapsed.TotalMilliseconds:F2}ms");
        
        return response;
    }
}
