using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class NotificationTests
{
    private static (IMediator Mediator, NotificationLog Log) CreateMediator(
        NotificationErrorHandling errorHandling = NotificationErrorHandling.ThrowAfterAllHandlers,
        ILoggerProvider? loggerProvider = null)
    {
        var log = new NotificationLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddLogging(builder =>
        {
            if (loggerProvider != null)
                builder.AddProvider(loggerProvider);
        });
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(NotificationTests).Assembly);
            cfg.NotificationErrorHandling = errorHandling;
        });

        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), log);
    }

    [Test]
    public async Task Publish_ShouldInvokeScannedHandlersInRegistrationOrder()
    {
        var (mediator, log) = CreateMediator();

        await mediator.Publish(new OrderPlaced(1));

        Assert.That(log.Entries, Is.EqualTo(new[] { "first:1", "second:1" }));
    }

    [Test]
    public async Task Publish_WithNoHandlers_ShouldComplete()
    {
        var (mediator, _) = CreateMediator();

        await mediator.Publish(new UnhandledNotification());
    }

    [Test]
    public void Publish_WithOneFailingHandler_ShouldRunAllHandlersThenRethrow()
    {
        var (mediator, log) = CreateMediator();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new PartlyFailing(1)));

        Assert.That(ex!.Message, Is.EqualTo("failing handler 1"));
        Assert.That(log.Entries, Is.EqualTo(new[] { "before", "after" }));
    }

    [Test]
    public void Publish_WithSeveralFailingHandlers_ShouldThrowAggregateException()
    {
        var (mediator, log) = CreateMediator();

        var ex = Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new PartlyFailing(2)));

        Assert.That(ex!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(log.Entries, Is.EqualTo(new[] { "before", "after" }));
    }

    [Test]
    public void Publish_WithStopOnFirstException_ShouldNotRunLaterHandlers()
    {
        var (mediator, log) = CreateMediator(NotificationErrorHandling.StopOnFirstException);

        Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new PartlyFailing(1)));

        Assert.That(log.Entries, Is.EqualTo(new[] { "before" }));
    }

    [Test]
    public async Task Publish_WithLogAndContinue_ShouldLogAndNotThrow()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var (mediator, log) = CreateMediator(NotificationErrorHandling.LogAndContinue, loggerProvider);

        await mediator.Publish(new PartlyFailing(2));

        Assert.That(log.Entries, Is.EqualTo(new[] { "before", "after" }));
        Assert.That(loggerProvider.Errors, Has.Count.EqualTo(2));
    }

    [Test]
    public void Publish_WhenCancelled_ShouldStopEvenWhenContinuingOnErrors()
    {
        var (mediator, log) = CreateMediator(NotificationErrorHandling.LogAndContinue);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => mediator.Publish(new CancellableNotification(), cts.Token));

        Assert.That(log.Entries, Is.Empty);
    }
}

public class NotificationLog
{
    public List<string> Entries { get; } = [];
}

public record OrderPlaced(int OrderId) : INotification;

public record UnhandledNotification : INotification;

/// <summary>Has handlers before, between and after <see cref="FailingCount"/> failing handlers.</summary>
public record PartlyFailing(int FailingCount) : INotification;

public record CancellableNotification : INotification;

public class FirstOrderPlacedHandler(NotificationLog log) : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add($"first:{notification.OrderId}");
        return Task.CompletedTask;
    }
}

public class SecondOrderPlacedHandler(NotificationLog log) : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add($"second:{notification.OrderId}");
        return Task.CompletedTask;
    }
}

// Scanning registers handlers in type declaration order: before, failing 1, failing 2, after
public class PartlyFailingBeforeHandler(NotificationLog log) : INotificationHandler<PartlyFailing>
{
    public Task Handle(PartlyFailing notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("before");
        return Task.CompletedTask;
    }
}

public class PartlyFailingFirstFailure : INotificationHandler<PartlyFailing>
{
    public async Task Handle(PartlyFailing notification, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw new InvalidOperationException("failing handler 1");
    }
}

public class PartlyFailingSecondFailure : INotificationHandler<PartlyFailing>
{
    public Task Handle(PartlyFailing notification, CancellationToken cancellationToken = default)
    {
        return notification.FailingCount >= 2
            ? throw new InvalidOperationException("failing handler 2")
            : Task.CompletedTask;
    }
}

public class PartlyFailingAfterHandler(NotificationLog log) : INotificationHandler<PartlyFailing>
{
    public Task Handle(PartlyFailing notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("after");
        return Task.CompletedTask;
    }
}

public class CancellableNotificationHandler(NotificationLog log) : INotificationHandler<CancellableNotification>
{
    public Task Handle(CancellableNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Entries.Add("handled");
        return Task.CompletedTask;
    }
}

public class SecondCancellableNotificationHandler(NotificationLog log) : INotificationHandler<CancellableNotification>
{
    public Task Handle(CancellableNotification notification, CancellationToken cancellationToken = default)
    {
        log.Entries.Add("second handled");
        return Task.CompletedTask;
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<Exception> Errors { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception != null)
            {
                provider.Errors.Add(exception);
            }
        }
    }
}
