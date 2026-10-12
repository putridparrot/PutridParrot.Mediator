using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class StreamBehaviorTests
{
    private static (IMediator Mediator, StreamLog Log) CreateMediator(Action<MediatorConfiguration> configure, ILoggerProvider? loggerProvider = null)
    {
        var log = new StreamLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(new PipelineLog());
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            if (loggerProvider != null)
                builder.AddProvider(loggerProvider);
        });
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(StreamBehaviorTests).Assembly);
            configure(cfg);
        });

        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), log);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> stream, CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }
        return items;
    }

    [Test]
    public async Task OpenStreamBehaviors_ShouldWrapTheStreamInRegistrationOrder()
    {
        var (mediator, log) = CreateMediator(cfg =>
        {
            cfg.AddOpenBehavior(typeof(OuterStreamBehavior<,>));
            cfg.AddOpenBehavior(typeof(InnerStreamBehavior<,>));
        });

        var items = await ToListAsync(mediator.CreateStream(new NumberStream(2)));

        Assert.That(items, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(log.Entries, Is.EqualTo(new[]
        {
            "outer:start", "inner:start", "handler:start",
            "inner:1", "outer:1", "inner:2", "outer:2",
            "inner:end", "outer:end"
        }));
    }

    [Test]
    public async Task ClosedStreamBehavior_ShouldOnlyApplyToItsRequestType()
    {
        var (mediator, _) = CreateMediator(cfg => cfg.AddBehavior<DoubleNumbersBehavior>());

        Assert.That(await ToListAsync(mediator.CreateStream(new NumberStream(3))), Is.EqualTo(new[] { 2, 4, 6 }));
        Assert.That(await ToListAsync(mediator.CreateStream(new OtherNumberStream(3))), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task RequestAndStreamBehaviors_ShouldOnlyRunForTheirOwnPipeline()
    {
        var log = new PipelineLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton(new StreamLog());
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(StreamBehaviorTests).Assembly);
            cfg.AddOpenBehavior(typeof(AnyRequestBehavior<,>));
            cfg.AddOpenBehavior(typeof(InnerStreamBehavior<,>));
        });
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var streamLog = provider.GetRequiredService<StreamLog>();

        await ToListAsync(mediator.CreateStream(new NumberStream(1)));
        Assert.That(log.Entries, Is.Empty, "Request behaviors must not run for streams");
        Assert.That(streamLog.Entries, Does.Contain("inner:start"));

        streamLog.Entries.Clear();
        await mediator.Send(new PipelinePing("x"));
        Assert.That(streamLog.Entries, Is.Empty, "Stream behaviors must not run for requests");
    }

    [Test]
    public async Task AddOpenBehavior_WithTypeImplementingBothInterfaces_ShouldRegisterForBothPipelines()
    {
        var (mediator, log) = CreateMediator(cfg => cfg.AddOpenBehavior(typeof(RequestAndStreamBehavior<,>)));

        await mediator.Send(new PipelinePing("x"));
        await ToListAsync(mediator.CreateStream(new NumberStream(1)));

        Assert.That(log.Entries, Is.EqualTo(new[] { "both:request", "both:stream", "handler:start" }));
    }

    [Test]
    public async Task ValidationStreamBehavior_ShouldValidateBeforeStreaming()
    {
        var (mediator, log) = CreateMediator(cfg => cfg.AddOpenBehavior(typeof(ValidationStreamBehavior<,>)));

        Assert.That(await ToListAsync(mediator.CreateStream(new NumberStream(2))), Is.EqualTo(new[] { 1, 2 }));

        log.Entries.Clear();
        var ex = Assert.ThrowsAsync<RequestValidationException>(() => ToListAsync(mediator.CreateStream(new NumberStream(-1))));
        Assert.That(ex!.Failures.Single().PropertyName, Is.EqualTo("Count"));
        Assert.That(log.Entries, Is.Empty, "The handler must not run for an invalid request");
    }

    [Test]
    public async Task LoggingStreamBehavior_ShouldLogStartAndCompletionWithItemCount()
    {
        var loggerProvider = new MessageCapturingLoggerProvider();
        var (mediator, _) = CreateMediator(cfg => cfg.AddOpenBehavior(typeof(LoggingStreamBehavior<,>)), loggerProvider);

        await ToListAsync(mediator.CreateStream(new NumberStream(3)));

        Assert.That(loggerProvider.Messages, Has.Some.EqualTo("Debug: Handling stream request: NumberStream"));
        Assert.That(loggerProvider.Messages, Has.Some.StartsWith("Debug: Stream request NumberStream completed with 3 items"));
    }

    [Test]
    public void LoggingStreamBehavior_ShouldLogFailures()
    {
        var loggerProvider = new MessageCapturingLoggerProvider();
        var (mediator, _) = CreateMediator(cfg => cfg.AddOpenBehavior(typeof(LoggingStreamBehavior<,>)), loggerProvider);

        Assert.ThrowsAsync<InvalidOperationException>(() => ToListAsync(mediator.CreateStream(new FailingNumberStream())));

        Assert.That(loggerProvider.Messages, Has.Some.StartsWith("Error: Stream request FailingNumberStream failed after 1 items"));
    }

    [Test]
    public void StreamBehaviors_ShouldPassCancellationThrough()
    {
        var (mediator, _) = CreateMediator(cfg =>
        {
            cfg.AddOpenBehavior(typeof(LoggingStreamBehavior<,>));
            cfg.AddOpenBehavior(typeof(InnerStreamBehavior<,>));
        });
        using var cts = new CancellationTokenSource();

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new EndlessNumberStream(), cts.Token))
            {
                if (item == 3)
                    cts.Cancel();
            }
        });
    }
}

public class StreamLog
{
    public List<string> Entries { get; } = [];
}

public record NumberStream(int Count) : IStreamRequest<int>;

public record OtherNumberStream(int Count) : IStreamRequest<int>;

public record FailingNumberStream : IStreamRequest<int>;

public record EndlessNumberStream : IStreamRequest<int>;

public class NumberStreamHandler(StreamLog log) : IStreamRequestHandler<NumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(NumberStream request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        log.Entries.Add("handler:start");
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public class OtherNumberStreamHandler : IStreamRequestHandler<OtherNumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(OtherNumberStream request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public class FailingNumberStreamHandler : IStreamRequestHandler<FailingNumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(FailingNumberStream request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return 1;
        throw new InvalidOperationException("stream broke");
    }
}

public class EndlessNumberStreamHandler : IStreamRequestHandler<EndlessNumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(EndlessNumberStream request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; ; i++)
        {
            await Task.Delay(1, cancellationToken);
            yield return i;
        }
    }
}

public class NumberStreamValidator : IRequestValidator<NumberStream>
{
    public Task<IReadOnlyList<ValidationFailure>> ValidateAsync(NumberStream request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ValidationFailure>>(request.Count < 0
            ? [new ValidationFailure(nameof(request.Count), "Count can't be negative")]
            : []);
}

public class OuterStreamBehavior<TRequest, TResponse>(StreamLog log) : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        log.Entries.Add("outer:start");
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            log.Entries.Add($"outer:{item}");
            yield return item;
        }
        log.Entries.Add("outer:end");
    }
}

public class InnerStreamBehavior<TRequest, TResponse>(StreamLog log) : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        log.Entries.Add("inner:start");
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            log.Entries.Add($"inner:{item}");
            yield return item;
        }
        log.Entries.Add("inner:end");
    }
}

/// <summary>A closed stream behavior that transforms the items of one stream type.</summary>
public class DoubleNumbersBehavior : IStreamPipelineBehavior<NumberStream, int>
{
    public async IAsyncEnumerable<int> Handle(NumberStream request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item * 2;
        }
    }
}

/// <summary>Implements both pipeline interfaces, so AddOpenBehavior registers it for requests and streams.</summary>
public class RequestAndStreamBehavior<TRequest, TResponse>(StreamLog log) : IPipelineBehavior<TRequest, TResponse>, IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("both:request");
        return next();
    }

    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("both:stream");
        return next();
    }
}

public sealed class MessageCapturingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(MessageCapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            provider.Messages.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
