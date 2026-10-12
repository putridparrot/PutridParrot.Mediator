using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class ValidationAndExceptionHandlingTests
{
    private static IServiceProvider CreateProvider(Action<MediatorConfiguration>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ValidationAndExceptionHandlingTests).Assembly);
            configure?.Invoke(cfg);
        });
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task ISenderAndIPublisher_ShouldBeRegistered()
    {
        var provider = CreateProvider();

        var sender = provider.GetRequiredService<ISender>();
        var publisher = provider.GetRequiredService<IPublisher>();

        Assert.That(await sender.Send(new CreateAccount("valid", 30)), Is.EqualTo("created valid"));
        await publisher.Publish(new UnhandledNotification());
    }

    [Test]
    public async Task ValidationBehavior_WithValidRequest_ShouldCallHandler()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))).GetRequiredService<IMediator>();

        Assert.That(await mediator.Send(new CreateAccount("valid", 30)), Is.EqualTo("created valid"));
    }

    [Test]
    public void ValidationBehavior_WithInvalidRequest_ShouldThrowWithFailuresFromAllValidators()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))).GetRequiredService<IMediator>();

        var ex = Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new CreateAccount("", 10)));

        Assert.That(ex!.RequestType, Is.EqualTo(typeof(CreateAccount)));
        Assert.That(ex.Failures.Select(f => f.PropertyName), Is.EquivalentTo(new[] { "Name", "Age" }));
        Assert.That(ex.Message, Does.Contain("Name: Name is required"));
    }

    [Test]
    public void ValidationBehavior_ShouldValidateCommands()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))).GetRequiredService<IMediator>();

        Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new DeleteAccount("")));
        Assert.DoesNotThrowAsync(() => mediator.Send(new DeleteAccount("someone")));
    }

    [Test]
    public async Task ExceptionHandlingBehavior_WhenHandled_ShouldReturnRecoveryResponse()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>))).GetRequiredService<IMediator>();

        Assert.That(await mediator.Send(new LookupAccount("missing")), Is.EqualTo("unknown"));
    }

    [Test]
    public void ExceptionHandlingBehavior_WhenNotHandled_ShouldRethrowOriginalException()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>))).GetRequiredService<IMediator>();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new LookupAccount("broken")));
        Assert.That(ex!.Message, Is.EqualTo("account store unavailable"));
    }

    [Test]
    public async Task ExceptionHandlingBehavior_AddedFirst_ShouldSeeValidationFailures()
    {
        var mediator = CreateProvider(cfg =>
        {
            cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        }).GetRequiredService<IMediator>();

        Assert.That(await mediator.Send(new CreateAccount("", 30)), Is.EqualTo("invalid account"));
    }

    [Test]
    public void ExceptionHandlingBehavior_ShouldNotHandleCancellation()
    {
        var mediator = CreateProvider(cfg => cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>))).GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => mediator.Send(new LookupAccount("missing"), cts.Token));
    }
}

public record CreateAccount(string Name, int Age) : IRequest<string>;

public record DeleteAccount(string Name) : IRequest;

public record LookupAccount(string Name) : IRequest<string>;

public class CreateAccountHandler : IRequestHandler<CreateAccount, string>
{
    public Task<string> Handle(CreateAccount request, CancellationToken cancellationToken = default) =>
        Task.FromResult($"created {request.Name}");
}

public class DeleteAccountHandler : IRequestHandler<DeleteAccount>
{
    public Task Handle(DeleteAccount request, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class LookupAccountHandler : IRequestHandler<LookupAccount, string>
{
    public Task<string> Handle(LookupAccount request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.Name switch
        {
            "missing" => throw new KeyNotFoundException(request.Name),
            "broken" => throw new InvalidOperationException("account store unavailable"),
            _ => Task.FromResult(request.Name)
        };
    }
}

public class CreateAccountNameValidator : IRequestValidator<CreateAccount>
{
    public Task<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateAccount request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ValidationFailure>>(string.IsNullOrWhiteSpace(request.Name)
            ? [new ValidationFailure(nameof(request.Name), "Name is required")]
            : []);
}

public class CreateAccountAgeValidator : IRequestValidator<CreateAccount>
{
    public Task<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateAccount request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ValidationFailure>>(request.Age < 18
            ? [new ValidationFailure(nameof(request.Age), "Must be an adult")]
            : []);
}

public class DeleteAccountValidator : IRequestValidator<DeleteAccount>
{
    public Task<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteAccount request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ValidationFailure>>(string.IsNullOrWhiteSpace(request.Name)
            ? [new ValidationFailure(nameof(request.Name), "Name is required")]
            : []);
}

public class LookupAccountNotFoundHandler : IRequestExceptionHandler<LookupAccount, string>
{
    public Task<RequestExceptionResult<string>> Handle(LookupAccount request, Exception exception, CancellationToken cancellationToken) =>
        Task.FromResult(exception is KeyNotFoundException
            ? RequestExceptionResult<string>.Handled("unknown")
            : RequestExceptionResult<string>.NotHandled);
}

public class CreateAccountInvalidHandler : IRequestExceptionHandler<CreateAccount, string>
{
    public Task<RequestExceptionResult<string>> Handle(CreateAccount request, Exception exception, CancellationToken cancellationToken) =>
        Task.FromResult(exception is RequestValidationException
            ? RequestExceptionResult<string>.Handled("invalid account")
            : RequestExceptionResult<string>.NotHandled);
}
