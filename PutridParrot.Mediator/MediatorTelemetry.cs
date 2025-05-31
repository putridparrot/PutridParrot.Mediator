using System.Diagnostics;

namespace PutridParrot.Mediator;

/// <summary>
/// Activity source for mediator telemetry.
/// Provides OpenTelemetry-compatible tracing for request/response operations.
/// </summary>
public static class MediatorActivitySource
{
    /// <summary>
    /// The name of the activity source for mediator operations.
    /// </summary>
    public const string SourceName = "PutridParrot.Mediator";
    
    /// <summary>
    /// The version of the mediator library.
    /// </summary>
    public const string Version = "1.0.0";
    
    /// <summary>
    /// The activity source for creating telemetry spans.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);
}

/// <summary>
/// Extension methods for adding telemetry to mediator operations.
/// </summary>
public static class MediatorTelemetryExtensions
{
    /// <summary>
    /// Creates an activity for tracking a request through the mediator.
    /// </summary>
    /// <param name="requestType">The type of the request.</param>
    /// <param name="operationName">The operation name (Send or Publish).</param>
    /// <returns>The created activity, or null if no listeners are registered.</returns>
    public static Activity? StartRequestActivity(Type requestType, string operationName = "Send")
    {
        var activity = MediatorActivitySource.Source.StartActivity(
            $"Mediator.{operationName}",
            ActivityKind.Internal);
        
        if (activity != null)
        {
            activity.SetTag("mediator.request_type", requestType.Name);
            activity.SetTag("mediator.request_full_name", requestType.FullName);
            activity.SetTag("mediator.operation", operationName);
        }
        
        return activity;
    }
    
    /// <summary>
    /// Records an error on the activity.
    /// </summary>
    public static void RecordError(this Activity? activity, Exception exception)
    {
        if (activity == null) return;
        
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("error.message", exception.Message);
        activity.SetTag("error.stacktrace", exception.StackTrace);
    }
}
