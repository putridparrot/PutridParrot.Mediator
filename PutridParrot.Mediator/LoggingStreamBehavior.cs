using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator;

/// <summary>
/// Stream pipeline behavior that logs streaming request execution: the start of the stream at Debug level,
/// the number of items and elapsed time when it completes, and any failure at Error level.
/// </summary>
/// <typeparam name="TRequest">The type of streaming request.</typeparam>
/// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
/// <example>
/// <code>
/// services.AddMediator(cfg =>
/// {
///     cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
///     cfg.AddOpenBehavior(typeof(LoggingStreamBehavior&lt;,&gt;));
/// });
/// </code>
/// </example>
public class LoggingStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingStreamBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the LoggingStreamBehavior class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingStreamBehavior(ILogger<LoggingStreamBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Streams the items of the next behavior or handler, logging the start, completion and any failure.
    /// </summary>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("Handling stream request: {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        var count = 0;

        // Enumerate manually: C# doesn't allow yield return inside a try block that has a catch
        await using var enumerator = next().GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Stream request {RequestName} failed after {Count} items with exception: {ExceptionMessage}",
                    requestName, count, ex.Message);
                throw;
            }

            count++;
            yield return enumerator.Current;
        }

        _logger.LogDebug(
            "Stream request {RequestName} completed with {Count} items in {ElapsedMs}ms",
            requestName, count, stopwatch.ElapsedMilliseconds);
    }
}
