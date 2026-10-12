using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Benchmarks;

public class Program
{
    // e.g. dotnet run -c Release -- --filter * --job short
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private IServiceProvider _provider = null!;
    private IMediator _generated = null!;
    private IMediator _fallback = null!;
    private TestRequest _request = null!;
    private TestCommand _command = null!;
    private TestNotification _notification = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Generated routes and registration: no reflection
        var generatedServices = new ServiceCollection();
        generatedServices.AddMediator();
        generatedServices.AddGeneratedMediatorHandlers();
        _provider = generatedServices.BuildServiceProvider();
        _generated = _provider.GetRequiredService<IMediator>();

        // Same handlers without the generated routes, so the mediator uses its runtime-built delegates
        var fallbackServices = new ServiceCollection();
        fallbackServices.AddMediator();
        fallbackServices.AddTransient<IRequestHandler<TestRequest, int>, TestRequestHandler>();
        fallbackServices.AddTransient<IRequestHandler<TestCommand>, TestCommandHandler>();
        fallbackServices.AddTransient<INotificationHandler<TestNotification>, TestNotificationHandler>();
        _fallback = fallbackServices.BuildServiceProvider().GetRequiredService<IMediator>();

        _request = new TestRequest(42);
        _command = new TestCommand();
        _notification = new TestNotification();
    }

    [Benchmark(Baseline = true, Description = "Resolve handler from DI and call it")]
    public Task<int> DirectCall()
    {
        return _provider.GetRequiredService<IRequestHandler<TestRequest, int>>().Handle(_request, CancellationToken.None);
    }

    [Benchmark(Description = "Send request (generated routes)")]
    public Task<int> Send_Generated() => _generated.Send(_request);

    [Benchmark(Description = "Send request (runtime fallback)")]
    public Task<int> Send_Fallback() => _fallback.Send(_request);

    [Benchmark(Description = "Send command (generated routes)")]
    public Task SendCommand_Generated() => _generated.Send(_command);

    [Benchmark(Description = "Send command (runtime fallback)")]
    public Task SendCommand_Fallback() => _fallback.Send(_command);

    [Benchmark(Description = "Publish notification (generated routes)")]
    public Task Publish_Generated() => _generated.Publish(_notification);

    [Benchmark(Description = "Publish notification (runtime fallback)")]
    public Task Publish_Fallback() => _fallback.Publish(_notification);
}

// Test types
public record TestRequest(int Value) : IRequest<int>;

public record TestCommand : IRequest;

public record TestNotification : INotification;

public class TestRequestHandler : IRequestHandler<TestRequest, int>
{
    public Task<int> Handle(TestRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}

public class TestCommandHandler : IRequestHandler<TestCommand>
{
    public Task Handle(TestCommand request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class TestNotificationHandler : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
