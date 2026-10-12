namespace PutridParrot.Mediator;

/// <summary>
/// Represents "no value". Used as the response type when a command (<see cref="IRequest"/>)
/// passes through the pipeline, so behaviors registered as <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c>
/// also run for commands, with <c>TResponse</c> = <see cref="Unit"/>.
/// </summary>
public readonly record struct Unit
{
    /// <summary>
    /// The single value of <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = default;

    /// <inheritdoc />
    public override string ToString() => "()";
}
