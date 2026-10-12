using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;
using PutridParrot.Mediator.AotSmokeTest;

// Exercises the reflection-free path end to end. Run as a Native AOT executable, where any
// accidental use of reflection-only features fails at runtime.
Console.WriteLine($"Dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}");

var services = new ServiceCollection();
services.AddSingleton<Log>();
services.AddMediator(cfg =>
{
    cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
services.AddGeneratedMediatorHandlers();
Unroutable.Register(services);

// Without open generic behaviors, value-type responses work under Native AOT too
var closedOnlyServices = new ServiceCollection();
closedOnlyServices.AddSingleton<Log>();
closedOnlyServices.AddMediator(cfg => cfg.AddBehavior<TimingBehavior>());
closedOnlyServices.AddGeneratedMediatorHandlers();
await using var closedOnlyProvider = closedOnlyServices.BuildServiceProvider();

await using var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();
var log = provider.GetRequiredService<Log>();
var failures = 0;

async Task Check(string name, Func<Task<string>> action, string expected)
{
    try
    {
        var actual = await action();
        var ok = actual == expected;
        failures += ok ? 0 : 1;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: {actual}{(ok ? "" : $" (expected {expected})")}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

await Check("request", () => mediator.Send(new Greet("AOT")), "Hello AOT");
await Check("validation handled by exception handler", () => mediator.Send(new Greet("")), "invalid greeting");
await Check("value-type response with closed behavior", async () =>
{
    var result = await closedOnlyProvider.GetRequiredService<IMediator>().Send(new Add(2, 3));
    return $"{result} {closedOnlyProvider.GetRequiredService<Log>().Read()}";
}, "5 timed Add");

// With open generic behaviors registered, the DI container can't create them for value types without dynamic code
await Check("value-type response with open behaviors", async () =>
{
    try
    {
        return (await mediator.Send(new Add(2, 3))).ToString();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("can't be created under Native AOT"))
    {
        return "clear AOT error";
    }
}, RuntimeFeature.IsDynamicCodeSupported ? "5" : "clear AOT error");
await Check("command", async () => { await mediator.Send(new Remember("x")); return log.Read(); }, "remembered x");
await Check("stream", async () =>
{
    var items = new List<int>();
    await foreach (var item in mediator.CreateStream(new CountTo(3)))
        items.Add(item);
    return string.Join(",", items);
}, "0,1,2");
await Check("notification", async () => { await mediator.Publish(new Announced("hi")); return log.Read(); }, "first hi,second hi");
await Check("ISender", () => provider.GetRequiredService<ISender>().Send(new Greet("sender")), "Hello sender");

// A handler the generator can't see has no route; without dynamic code that must fail clearly
await Check("unroutable request", async () =>
{
    try
    {
        return await mediator.Send(new Unroutable.Hidden());
    }
    catch (InvalidOperationException ex) when (ex.Message.StartsWith("No generated mediator route"))
    {
        return RuntimeFeature.IsDynamicCodeSupported ? "unexpected" : "clear AOT error";
    }
}, RuntimeFeature.IsDynamicCodeSupported ? "hidden via fallback" : "clear AOT error");

Console.WriteLine(failures == 0 ? "All checks passed" : $"{failures} check(s) failed");
return failures == 0 ? 0 : 1;

namespace PutridParrot.Mediator.AotSmokeTest
{
    public sealed class Log
    {
        private readonly List<string> _entries = [];

        public void Add(string entry) => _entries.Add(entry);

        public string Read()
        {
            var result = string.Join(",", _entries);
            _entries.Clear();
            return result;
        }
    }

    public record Greet(string Name) : IRequest<string>;

    public class GreetHandler : IRequestHandler<Greet, string>
    {
        public Task<string> Handle(Greet request, CancellationToken cancellationToken = default) => Task.FromResult($"Hello {request.Name}");
    }

    public class GreetValidator : IRequestValidator<Greet>
    {
        public Task<IReadOnlyList<ValidationFailure>> ValidateAsync(Greet request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ValidationFailure>>(string.IsNullOrEmpty(request.Name)
                ? [new ValidationFailure(nameof(request.Name), "Name is required")]
                : []);
    }

    public class GreetInvalidHandler : IRequestExceptionHandler<Greet, string>
    {
        public Task<RequestExceptionResult<string>> Handle(Greet request, Exception exception, CancellationToken cancellationToken) =>
            Task.FromResult(exception is RequestValidationException
                ? RequestExceptionResult<string>.Handled("invalid greeting")
                : RequestExceptionResult<string>.NotHandled);
    }

    public record Add(int A, int B) : IRequest<int>;

    public class AddHandler : IRequestHandler<Add, int>
    {
        public Task<int> Handle(Add request, CancellationToken cancellationToken = default) => Task.FromResult(request.A + request.B);
    }

    /// <summary>A closed behavior, for a request with a value-type response.</summary>
    public class TimingBehavior(Log log) : IPipelineBehavior<Add, int>
    {
        public async Task<int> Handle(Add request, RequestHandlerDelegate<int> next, CancellationToken cancellationToken)
        {
            var result = await next();
            log.Add("timed Add");
            return result;
        }
    }

    public record Remember(string Value) : IRequest;

    public class RememberHandler(Log log) : IRequestHandler<Remember>
    {
        public Task Handle(Remember request, CancellationToken cancellationToken = default)
        {
            log.Add($"remembered {request.Value}");
            return Task.CompletedTask;
        }
    }

    public record CountTo(int Count) : IStreamRequest<int>;

    public class CountToHandler : IStreamRequestHandler<CountTo, int>
    {
        public async IAsyncEnumerable<int> Handle(CountTo request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    public record Announced(string Text) : INotification;

    public class FirstAnnouncedHandler(Log log) : INotificationHandler<Announced>
    {
        public Task Handle(Announced notification, CancellationToken cancellationToken = default)
        {
            log.Add($"first {notification.Text}");
            return Task.CompletedTask;
        }
    }

    public class SecondAnnouncedHandler(Log log) : INotificationHandler<Announced>
    {
        public Task Handle(Announced notification, CancellationToken cancellationToken = default)
        {
            log.Add($"second {notification.Text}");
            return Task.CompletedTask;
        }
    }

    public static class Unroutable
    {
        public record Hidden : IRequest<string>;

        // Private, so the generator skips it and there is no generated route
        private class HiddenHandler : IRequestHandler<Hidden, string>
        {
            public Task<string> Handle(Hidden request, CancellationToken cancellationToken = default) => Task.FromResult("hidden via fallback");
        }

        public static void Register(IServiceCollection services) =>
            services.AddTransient<IRequestHandler<Hidden, string>, HiddenHandler>();
    }
}
