using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace PutridParrot.Mediator.SourceGenerator.Tests;

[TestFixture]
public class MediatorSourceGeneratorTests
{
    private const string BasicHandlers = """
        namespace App;

        public record Ping(string Text) : IRequest<string>;
        public class PingHandler : IRequestHandler<Ping, string>
        {
            public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("pong:" + request.Text);
        }

        public record DoWork : IRequest;
        public class DoWorkHandler : IRequestHandler<DoWork>
        {
            public Task Handle(DoWork request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        public record Count(int N) : IStreamRequest<int>;
        public class CountHandler : IStreamRequestHandler<Count, int>
        {
            public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                for (var i = 0; i < request.N; i++) { await Task.Yield(); yield return i; }
            }
        }
        """;

    [Test]
    public void Generate_WithHandlers_ShouldProduceCodeThatCompilesWithoutWarnings()
    {
        var result = GeneratorTestHelper.Run(BasicHandlers);

        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilationProblems, Is.Empty);
        Assert.That(result.GeneratedSources.Keys, Is.EquivalentTo(new[]
        {
            "MediatorExtensions.g.cs", "GeneratedRequestDispatcher.g.cs", "GeneratedStreamDispatcher.g.cs"
        }));
    }

    [Test]
    public void Generate_ShouldMakeGeneratedTypesInternal()
    {
        var result = GeneratorTestHelper.Run(BasicHandlers);

        foreach (var name in new[] { "GeneratedMediatorExtensions", "GeneratedRequestDispatcher", "GeneratedStreamDispatcher" })
        {
            var type = result.Type($"PutridParrot.Mediator.{name}");
            Assert.That(type, Is.Not.Null, name);
            Assert.That(type!.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal), name);
        }
    }

    [Test]
    public void Generate_WithNoHandlers_ShouldProduceNothing()
    {
        var result = GeneratorTestHelper.Run("namespace App; public class NotAHandler : IDisposable { public void Dispose() { } }");

        Assert.That(result.GeneratedSources, Is.Empty);
        Assert.That(result.CompilationProblems, Is.Empty);
    }

    [Test]
    public void Generate_WithNullableAndGenericResponseTypes_ShouldCompileWithoutWarnings()
    {
        var result = GeneratorTestHelper.Run("""
            namespace App;

            public record Find(int Id) : IRequest<string?>;
            public class FindHandler : IRequestHandler<Find, string?>
            {
                public Task<string?> Handle(Find request, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
            }

            public record ListAll : IRequest<IReadOnlyList<KeyValuePair<int, string?>>>;
            public class ListAllHandler : IRequestHandler<ListAll, IReadOnlyList<KeyValuePair<int, string?>>>
            {
                public Task<IReadOnlyList<KeyValuePair<int, string?>>> Handle(ListAll request, CancellationToken cancellationToken = default) =>
                    Task.FromResult<IReadOnlyList<KeyValuePair<int, string?>>>([]);
            }
            """);

        Assert.That(result.CompilationProblems, Is.Empty);
        Assert.That(result.Source("GeneratedRequestDispatcher.g.cs"), Does.Contain("IRequestHandler<global::App.Find, string?>"));
    }

    [Test]
    public void Generate_WithTypesInGlobalNamespace_ShouldCompile()
    {
        var result = GeneratorTestHelper.Run("""
            public record GlobalPing : IRequest<int>;
            public class GlobalPingHandler : IRequestHandler<GlobalPing, int>
            {
                public Task<int> Handle(GlobalPing request, CancellationToken cancellationToken = default) => Task.FromResult(1);
            }
            """);

        Assert.That(result.CompilationProblems, Is.Empty);
        Assert.That(result.Source("GeneratedRequestDispatcher.g.cs"), Does.Contain("typeof(global::GlobalPing)"));
    }

    [Test]
    public void Generate_ShouldSkipHandlersThatCannotBeRegisteredOrAccessed()
    {
        var result = GeneratorTestHelper.Run("""
            namespace App;

            public record A : IRequest<int>;
            public record B : IRequest<int>;
            public record C<T> : IRequest<T>;
            public record D : IRequest<int>;

            public abstract class AbstractHandler : IRequestHandler<A, int>
            {
                public abstract Task<int> Handle(A request, CancellationToken cancellationToken = default);
            }

            public class Outer
            {
                private class PrivateHandler : IRequestHandler<B, int>
                {
                    public Task<int> Handle(B request, CancellationToken cancellationToken = default) => Task.FromResult(2);
                }
            }

            public class GenericHandler<T> : IRequestHandler<C<T>, T>
            {
                public Task<T> Handle(C<T> request, CancellationToken cancellationToken = default) => Task.FromResult(default(T)!);
            }

            public class DHandler : IRequestHandler<D, int>
            {
                public Task<int> Handle(D request, CancellationToken cancellationToken = default) => Task.FromResult(4);
            }
            """);

        Assert.That(result.CompilationProblems, Is.Empty);

        var dispatcher = result.Source("GeneratedRequestDispatcher.g.cs");
        Assert.That(dispatcher, Does.Contain("typeof(global::App.D)"));
        Assert.That(dispatcher, Does.Not.Contain("global::App.A"));
        Assert.That(dispatcher, Does.Not.Contain("global::App.B"));
        Assert.That(dispatcher, Does.Not.Contain("global::App.C"));
    }

    [Test]
    public void Generate_WithPartialHandler_ShouldRegisterItOnce()
    {
        var result = GeneratorTestHelper.Run("""
            namespace App;

            public record Ping : IRequest<int>;
            public partial class PingHandler : IRequestHandler<Ping, int>
            {
                public Task<int> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult(1);
            }
            public partial class PingHandler : IDisposable
            {
                public void Dispose() { }
            }
            """);

        Assert.That(result.GeneratorDiagnostics, Is.Empty);
        Assert.That(result.CompilationProblems, Is.Empty);

        var registration = result.Source("MediatorExtensions.g.cs");
        Assert.That(registration.Split("global::App.PingHandler").Length - 1, Is.EqualTo(1));
    }

    [Test]
    public void Generate_WithDuplicateHandlers_ShouldReportWarningAndStillCompile()
    {
        var result = GeneratorTestHelper.Run("""
            namespace App;

            public record Ping : IRequest<int>;
            public class FirstPingHandler : IRequestHandler<Ping, int>
            {
                public Task<int> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult(1);
            }
            public class SecondPingHandler : IRequestHandler<Ping, int>
            {
                public Task<int> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult(2);
            }
            """);

        var diagnostic = result.GeneratorDiagnostics.Single();
        Assert.That(diagnostic.Id, Is.EqualTo("PPMED001"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(diagnostic.GetMessage(), Does.Contain("App.Ping").And.Contain("App.FirstPingHandler").And.Contain("App.SecondPingHandler"));

        // One switch case per request type, otherwise the generated dispatcher wouldn't compile
        Assert.That(result.CompilationProblems, Is.Empty);
    }

    [Test]
    public async Task GeneratedCode_ShouldRegisterHandlersAndDispatchersThatWorkAtRuntime()
    {
        // Everything in the global namespace so Entry can be found by name after loading the assembly
        var result = GeneratorTestHelper.Run((BasicHandlers + """

            public static class Entry
            {
                public static async Task<string> Run()
                {
                    var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                    services.AddMediator();
                    services.AddGeneratedRequestHandlers();
                    var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

                    var dispatcher = (IRequestDispatcher)provider.GetService(typeof(IRequestDispatcher))!;
                    var streamDispatcher = (IStreamDispatcher)provider.GetService(typeof(IStreamDispatcher))!;
                    var mediator = (IMediator)provider.GetService(typeof(IMediator))!;

                    var pong = await mediator.Send(new Ping("hi"));
                    await mediator.Send(new DoWork());
                    var items = new List<int>();
                    await foreach (var i in mediator.CreateStream(new Count(3))) items.Add(i);

                    return string.Join("|",
                        dispatcher.GetType().Name, dispatcher.CanDispatch(typeof(Ping)), dispatcher.CanDispatch(typeof(string)),
                        streamDispatcher.GetType().Name, pong, string.Join(",", items));
                }
            }
            """).Replace("namespace App;", ""));

        Assert.That(result.CompilationProblems, Is.Empty);

        using var stream = new MemoryStream();
        var emit = result.Output.Emit(stream);
        Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));

        var context = new AssemblyLoadContext("GeneratorTest", isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetType("Entry")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            var output = await (Task<string>)run.Invoke(null, null)!;

            Assert.That(output, Is.EqualTo("GeneratedRequestDispatcher|True|False|GeneratedStreamDispatcher|pong:hi|0,1,2"));
        }
        finally
        {
            context.Unload();
        }
    }
}
