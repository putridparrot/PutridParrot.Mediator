using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Example.Streaming;

/// <summary>
/// Demonstrates streaming requests, including cancellation.
/// </summary>
public static class StreamingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("??????????????????????????????????????????????");
        Console.WriteLine("?  Streaming Requests - Demo Program        ?");
        Console.WriteLine("??????????????????????????????????????????????\n");

        // Setup
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddMediator(typeof(Program).Assembly);

        await using var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // ========================================
        // Example 1: Stream of Numbers
        // ========================================
        Console.WriteLine("=== Example 1: Stream Numbers (1-10) ===");
        var numberRequest = new StreamNumbersQuery(1, 10);

        await foreach (var number in mediator.CreateStream(numberRequest))
        {
            Console.WriteLine($"  Received: {number}");
        }
        Console.WriteLine();

        // ========================================
        // Example 2: Paginated Orders Stream
        // ========================================
        Console.WriteLine("=== Example 2: Stream Orders (Paginated) ===");
        var ordersRequest = new GetOrdersStreamQuery(CustomerId: 123);

        await foreach (var order in mediator.CreateStream(ordersRequest))
        {
            Console.WriteLine($"  Order #{order.OrderId}: ${order.Total:F2} - {order.Status}");
        }
        Console.WriteLine();

        // ========================================
        // Example 3: Progress Reporting Stream
        // ========================================
        Console.WriteLine("=== Example 3: Long-Running Task with Progress ===");
        var processRequest = new ProcessDataStreamQuery(ItemCount: 20);


        await foreach (var progress in mediator.CreateStream(processRequest))
        {
            Console.Write($"\r  Progress: {progress.Percentage:F1}% [{progress.Processed}/{progress.Total}]");
        }
        Console.WriteLine("\n  ? Processing complete!\n");

        // ========================================
        // Example 4: Cancellation Support
        // ========================================
        Console.WriteLine("=== Example 4: Stream with Cancellation (5 seconds) ===");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            var infiniteRequest = new InfiniteNumbersQuery();
            var count = 0;
            
            await foreach (var number in mediator.CreateStream(infiniteRequest, cts.Token))
            {
                Console.WriteLine($"  Number: {number}");
                count++;
                
                if (count >= 10)
                    break; // Stop after 10 items
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  ??  Stream was cancelled");
        }

        Console.WriteLine("\n? All streaming examples completed!\n");
    }
}

// ========================================
// STREAM REQUESTS & HANDLERS
// ========================================

/// <summary>
/// Request to stream a sequence of numbers.
/// </summary>
public record StreamNumbersQuery(int Start, int End) : IStreamRequest<int>;

public class StreamNumbersHandler : IStreamRequestHandler<StreamNumbersQuery, int>
{
    public async IAsyncEnumerable<int> Handle(
        StreamNumbersQuery request, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = request.Start; i <= request.End; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Simulate async work
            await Task.Delay(100, cancellationToken);
            
            yield return i;
        }
    }
}

/// <summary>
/// Request to stream orders for a customer (simulates pagination).
/// </summary>
public record GetOrdersStreamQuery(int CustomerId) : IStreamRequest<OrderDto>;

public record OrderDto(int OrderId, decimal Total, string Status);

public class GetOrdersStreamHandler : IStreamRequestHandler<GetOrdersStreamQuery, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetOrdersStreamQuery request, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simulate paginated database queries
        var pageSize = 5;
        var totalPages = 3;
        
        for (int page = 0; page < totalPages; page++)
        {
            // Simulate database call
            await Task.Delay(200, cancellationToken);
            
            // Yield orders from this page
            for (int i = 0; i < pageSize; i++)
            {
                var orderId = (page * pageSize) + i + 1;
                var total = Random.Shared.Next(10, 500);
                var status = Random.Shared.Next(0, 2) == 0 ? "Completed" : "Pending";
                
                yield return new OrderDto(orderId, total, status);
            }
        }
    }
}

/// <summary>
/// Request to stream progress updates for a long-running process.
/// </summary>
public record ProcessDataStreamQuery(int ItemCount) : IStreamRequest<ProgressUpdate>;

public record ProgressUpdate(int Processed, int Total, double Percentage);

public class ProcessDataStreamHandler : IStreamRequestHandler<ProcessDataStreamQuery, ProgressUpdate>
{
    public async IAsyncEnumerable<ProgressUpdate> Handle(
        ProcessDataStreamQuery request, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 1; i <= request.ItemCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Simulate processing work
            await Task.Delay(150, cancellationToken);
            
            var percentage = (i / (double)request.ItemCount) * 100;
            
            yield return new ProgressUpdate(i, request.ItemCount, percentage);
        }
    }
}

/// <summary>
/// Request to stream an infinite sequence (for testing cancellation).
/// </summary>
public record InfiniteNumbersQuery : IStreamRequest<int>;

public class InfiniteNumbersHandler : IStreamRequestHandler<InfiniteNumbersQuery, int>
{
    public async IAsyncEnumerable<int> Handle(
        InfiniteNumbersQuery request, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int number = 1;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken);
            yield return number++;
        }
    }
}
