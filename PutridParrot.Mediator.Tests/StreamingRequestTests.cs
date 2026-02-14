using Microsoft.Extensions.DependencyInjection;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class StreamingRequestTests
{
    [Test]
    public async Task CreateStream_WithValidStreamRequest_ShouldReturnItems()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(StreamingRequestTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new TestStreamRequest(5);
        var results = new List<int>();

        // Act
        await foreach (var item in mediator.CreateStream(request))
        {
            results.Add(item);
        }

        // Assert
        Assert.That(results, Has.Count.EqualTo(5));
        Assert.That(results, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task CreateStream_WithCancellation_ShouldStopStream()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(StreamingRequestTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new TestStreamRequest(100);
        var results = new List<int>();
        using var cts = new CancellationTokenSource();

        // Act
        try
        {
            await foreach (var item in mediator.CreateStream(request, cts.Token))
            {
                results.Add(item);
                
                if (results.Count == 3)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }

        // Assert
        Assert.That(results, Has.Count.EqualTo(3));
    }


    [Test]
    public void CreateStream_WithNoRegisteredHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, Mediator>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new TestStreamRequest(5);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(request))
            {
                // Should throw before getting here
            }
        });
    }

    [Test]
    public async Task CreateStream_WithEmptyStream_ShouldReturnNoItems()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(StreamingRequestTests).Assembly);
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new TestStreamRequest(0);
        var results = new List<int>();

        // Act
        await foreach (var item in mediator.CreateStream(request))
        {
            results.Add(item);
        }

        // Assert
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task CreateStream_WithException_ShouldPropagateException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<ErrorStreamRequest, int>, ErrorStreamHandler>();
        services.AddSingleton<IMediator, Mediator>();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();
        
        var request = new ErrorStreamRequest();

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(request))
            {
                // Exception should be thrown
            }
        });

        Assert.That(exception!.Message, Is.EqualTo("Test error in stream"));
    }
}

// Test types
public record TestStreamRequest(int Count) : IStreamRequest<int>;

public class TestStreamHandler : IStreamRequestHandler<TestStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        TestStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 1; i <= request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
            yield return i;
        }
    }
}

public record ErrorStreamRequest : IStreamRequest<int>;

public class ErrorStreamHandler : IStreamRequestHandler<ErrorStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        ErrorStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        yield return 1;
        
        throw new InvalidOperationException("Test error in stream");
        
        #pragma warning disable CS0162
        yield return 2; // Never reached
        #pragma warning restore CS0162
    }
}
