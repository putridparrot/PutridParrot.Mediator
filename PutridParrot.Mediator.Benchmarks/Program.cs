using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<MediatorBenchmarks>();
    }
}

[MemoryDiagnoser]
[RankColumn]
public class MediatorBenchmarks
{
    private IMediator _mediatorWithGenerator = null!;
    private IMediator _mediatorWithoutGenerator = null!;
    private TestRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup mediator WITH source generator (fast path)
        var servicesWithGen = new ServiceCollection();
        servicesWithGen.AddMediator(typeof(MediatorBenchmarks).Assembly);
        var spWithGen = servicesWithGen.BuildServiceProvider();
        _mediatorWithGenerator = spWithGen.GetRequiredService<IMediator>();

        // Setup mediator WITHOUT source generator (reflection path)
        var servicesWithoutGen = new ServiceCollection();
        servicesWithoutGen.AddSingleton<IMediator>(sp => new Mediator(sp, null));
        servicesWithoutGen.AddTransient<IRequestHandler<TestRequest, int>, TestRequestHandler>();
        var spWithoutGen = servicesWithoutGen.BuildServiceProvider();
        _mediatorWithoutGenerator = spWithoutGen.GetRequiredService<IMediator>();

        _request = new TestRequest(42);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Mediator_WithReflection()
    {
        return await _mediatorWithoutGenerator.Send(_request);
    }

    [Benchmark]
    public async Task<int> Mediator_WithSourceGenerator()
    {
        return await _mediatorWithGenerator.Send(_request);
    }

    [Benchmark]
    public async Task<int> DirectCall_Baseline()
    {
        var handler = new TestRequestHandler();
        return await handler.Handle(_request, CancellationToken.None);
    }
}

// Test types
public record TestRequest(int Value) : IRequest<int>;

public class TestRequestHandler : IRequestHandler<TestRequest, int>
{
    public Task<int> Handle(TestRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.Value * 2);
    }
}
