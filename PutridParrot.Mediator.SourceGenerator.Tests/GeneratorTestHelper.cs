using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace PutridParrot.Mediator.SourceGenerator.Tests;

/// <summary>
/// Compiles C# source with the <see cref="MediatorSourceGenerator"/> and returns the results.
/// </summary>
internal static class GeneratorTestHelper
{
    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using PutridParrot.Mediator;

        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var frameworkAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        var libraryAssemblies = new[]
            {
                typeof(IMediator).Assembly,
                typeof(IServiceCollection).Assembly,
                typeof(ServiceCollection).Assembly,
                typeof(ServiceProvider).Assembly
            }
            .Select(a => a.Location)
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        return frameworkAssemblies.Concat(libraryAssemblies).ToList();
    });

    public sealed record Result(
        Compilation Output,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        IReadOnlyDictionary<string, string> GeneratedSources)
    {
        /// <summary>Errors and warnings from compiling the source plus the generated code.</summary>
        public IReadOnlyList<Diagnostic> CompilationProblems =>
            Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();

        public string Source(string hintName) => GeneratedSources[hintName];

        public INamedTypeSymbol? Type(string metadataName) => Output.GetTypeByMetadataName(metadataName);
    }

    public static Result Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [CSharpSyntaxTree.ParseText(CommonUsings + source, new CSharpParseOptions(LanguageVersion.CSharp12))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MediatorSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generated = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString());

        return new Result(output, diagnostics, generated);
    }
}
