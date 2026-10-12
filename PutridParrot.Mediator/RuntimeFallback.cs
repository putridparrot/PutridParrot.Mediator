using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PutridParrot.Mediator;

/// <summary>
/// Invokes <see cref="MediatorRuntime"/> for types no generated route knows about, using delegates
/// built once per type with reflection. Not available under Native AOT.
/// </summary>
internal static class RuntimeFallback
{
    private const string TrimmingJustification =
        "The generic arguments are the runtime types of live request/notification objects and the call's response type, " +
        "so they exist at runtime. The adapter methods are found by constant name on this type, which the trimmer preserves.";

    private static readonly ConcurrentDictionary<(Type Request, Type Response, string Adapter), Delegate> Delegates = new();

    public static Task<TResponse> Send<TResponse>(IRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken)
    {
        var send = (Func<MediatorContext, IRequest<TResponse>, CancellationToken, Task<TResponse>>)
            GetDelegate(request.GetType(), typeof(TResponse), nameof(SendAdapter),
                typeof(Func<MediatorContext, IRequest<TResponse>, CancellationToken, Task<TResponse>>));
        return send(context, request, cancellationToken);
    }

    public static Task SendCommand(IRequest request, MediatorContext context, CancellationToken cancellationToken)
    {
        var send = (Func<MediatorContext, IRequest, CancellationToken, Task>)
            GetDelegate(request.GetType(), typeof(void), nameof(SendCommandAdapter),
                typeof(Func<MediatorContext, IRequest, CancellationToken, Task>));
        return send(context, request, cancellationToken);
    }

    public static IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, MediatorContext context, CancellationToken cancellationToken)
    {
        var create = (Func<MediatorContext, IStreamRequest<TResponse>, CancellationToken, IAsyncEnumerable<TResponse>>)
            GetDelegate(request.GetType(), typeof(TResponse), nameof(CreateStreamAdapter),
                typeof(Func<MediatorContext, IStreamRequest<TResponse>, CancellationToken, IAsyncEnumerable<TResponse>>));
        return create(context, request, cancellationToken);
    }

    public static Task Publish(INotification notification, MediatorContext context, CancellationToken cancellationToken)
    {
        var publish = (Func<MediatorContext, INotification, CancellationToken, Task>)
            GetDelegate(notification.GetType(), typeof(void), nameof(PublishAdapter),
                typeof(Func<MediatorContext, INotification, CancellationToken, Task>));
        return publish(context, notification, cancellationToken);
    }

    private static Delegate GetDelegate(Type requestType, Type responseType, string adapterName, Type delegateType)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new InvalidOperationException(
                $"No generated mediator route for {requestType.FullName}. Under Native AOT every request, stream and " +
                "notification type must have its handler in a project that references PutridParrot.Mediator, so the " +
                "source generator can create its route.");
        }

        // The adapter is part of the key because one class can be, for example, both a command and a notification
        return Delegates.GetOrAdd((requestType, responseType, adapterName),
            _ => CreateDelegate(requestType, responseType, adapterName, delegateType));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = TrimmingJustification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only reached when RuntimeFeature.IsDynamicCodeSupported is true.")]
    private static Delegate CreateDelegate(Type requestType, Type responseType, string adapterName, Type delegateType)
    {
        var typeArguments = responseType == typeof(void) ? new[] { requestType } : new[] { requestType, responseType };

        return typeof(RuntimeFallback)
            .GetMethod(adapterName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .CreateDelegate(delegateType);
    }

    private static Task<TResponse> SendAdapter<TRequest, TResponse>(MediatorContext context, IRequest<TResponse> request, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse> =>
        MediatorRuntime.Send<TRequest, TResponse>(context, (TRequest)request, cancellationToken);

    private static Task SendCommandAdapter<TRequest>(MediatorContext context, IRequest request, CancellationToken cancellationToken)
        where TRequest : IRequest =>
        MediatorRuntime.SendCommand(context, (TRequest)request, cancellationToken);

    private static IAsyncEnumerable<TResponse> CreateStreamAdapter<TRequest, TResponse>(MediatorContext context, IStreamRequest<TResponse> request, CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse> =>
        MediatorRuntime.CreateStream<TRequest, TResponse>(context, (TRequest)request, cancellationToken);

    private static Task PublishAdapter<TNotification>(MediatorContext context, INotification notification, CancellationToken cancellationToken)
        where TNotification : INotification =>
        MediatorRuntime.Publish(context, (TNotification)notification, cancellationToken);
}
