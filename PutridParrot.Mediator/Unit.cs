namespace PutridParrot.Mediator;

/// <summary>
/// Represents "no value". Used as the response type when a command (<see cref="IRequest"/>)
/// passes through the pipeline, so behaviors registered as <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c>
/// also run for commands, with <c>TResponse</c> = <see cref="Unit"/>.
/// </summary>
/// <remarks>
/// A class rather than a struct: under Native AOT the dependency injection container cannot close
/// open generic behaviors over value types, and every command's pipeline uses <see cref="Unit"/>.
/// </remarks>
public sealed class Unit : IEquatable<Unit>
{
    private Unit()
    {
    }

    /// <summary>
    /// The single value of <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = new();

    /// <inheritdoc />
    public bool Equals(Unit? other) => other is not null;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public override string ToString() => "()";
}
