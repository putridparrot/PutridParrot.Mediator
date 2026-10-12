using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace PutridParrot.Mediator.SourceGenerator;

/// <summary>
/// Source generator that automatically discovers request handlers and generates extension methods
/// for registering them with dependency injection at compile time.
/// </summary>
/// <remarks>
/// This generator scans the compilation for classes implementing IRequestHandler interfaces
/// and generates a static extension method to register all discovered handlers with the service collection.
/// This eliminates the need for runtime reflection when registering handlers.
/// </remarks>
[Generator]
public class MediatorSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Reported when more than one handler handles the same request type; the dependency
    /// injection container only uses the last one registered.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateHandler = new(
        id: "PPMED001",
        title: "Multiple handlers for the same request type",
        messageFormat: "Request type '{0}' has more than one handler ({1}); only the last one registered will be used",
        category: "PutridParrot.Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Initializes the incremental generator pipeline.
    /// </summary>
    /// <param name="context">The incremental generator initialization context.</param>
    /// <remarks>
    /// Class declarations with a base list are filtered syntactically, then each is analyzed
    /// semantically for IRequestHandler / IStreamRequestHandler implementations. The results are
    /// value-equatable so the IDE only regenerates when the set of handlers actually changes.
    /// </remarks>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, ct) => GetHandlers(ctx, ct))
            .SelectMany(static (items, _) => items)
            .Collect();

        context.RegisterSourceOutput(handlers, static (spc, items) => Execute(spc, items));
    }

    private static ImmutableArray<HandlerInfo> GetHandlers(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        // Abstract and open generic classes can't be registered directly, and private/protected
        // nested classes aren't accessible from the generated code; the runtime scan still covers those
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol classSymbol ||
            classSymbol.IsAbstract ||
            classSymbol.IsGenericType ||
            !IsAccessibleFromAssembly(classSymbol))
        {
            return ImmutableArray<HandlerInfo>.Empty;
        }

        // Partial classes have several declarations; only report the handler once
        if (classSymbol.DeclaringSyntaxReferences.Length > 1 &&
            classSymbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) != context.Node)
        {
            return ImmutableArray<HandlerInfo>.Empty;
        }

        var handlerType = Name(classSymbol);
        var result = ImmutableArray.CreateBuilder<HandlerInfo>();

        foreach (var @interface in classSymbol.AllInterfaces)
        {
            if (!@interface.IsGenericType)
                continue;

            switch (@interface.ConstructedFrom.ToDisplayString())
            {
                case "PutridParrot.Mediator.IRequestHandler<TRequest, TResponse>":
                    result.Add(new HandlerInfo(HandlerKind.Request, handlerType,
                        Name(@interface.TypeArguments[0]), Name(@interface.TypeArguments[1])));
                    break;
                case "PutridParrot.Mediator.IRequestHandler<TRequest>":
                    result.Add(new HandlerInfo(HandlerKind.RequestNoResponse, handlerType,
                        Name(@interface.TypeArguments[0]), string.Empty));
                    break;
                case "PutridParrot.Mediator.IStreamRequestHandler<TRequest, TResponse>":
                    result.Add(new HandlerInfo(HandlerKind.Stream, handlerType,
                        Name(@interface.TypeArguments[0]), Name(@interface.TypeArguments[1])));
                    break;
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Generates the registration code and dispatchers for all discovered handlers.
    /// </summary>
    /// <remarks>
    /// Adds "MediatorExtensions.g.cs", "GeneratedRequestDispatcher.g.cs" and "GeneratedStreamDispatcher.g.cs"
    /// to the compilation as appropriate. Pipeline behaviors need no generated code; the Mediator resolves
    /// them from dependency injection at runtime.
    /// </remarks>
    private static void Execute(SourceProductionContext context, ImmutableArray<HandlerInfo> handlers)
    {
        var requestHandlers = handlers.Where(h => h.Kind != HandlerKind.Stream).ToList();
        var streamHandlers = handlers.Where(h => h.Kind == HandlerKind.Stream).ToList();

        ReportDuplicateHandlers(context, requestHandlers);
        ReportDuplicateHandlers(context, streamHandlers);

        if (requestHandlers.Count > 0 || streamHandlers.Count > 0)
        {
            var registrationSource = GenerateExtensionClass(requestHandlers, streamHandlers);
            context.AddSource("MediatorExtensions.g.cs", SourceText.From(registrationSource, Encoding.UTF8));
        }

        // The dispatchers need one case per request type; duplicates would be unreachable switch cases
        if (requestHandlers.Count > 0)
        {
            var dispatcherSource = GenerateDispatcher(DistinctByRequestType(requestHandlers));
            context.AddSource("GeneratedRequestDispatcher.g.cs", SourceText.From(dispatcherSource, Encoding.UTF8));
        }

        if (streamHandlers.Count > 0)
        {
            var streamDispatcherSource = GenerateStreamDispatcher(DistinctByRequestType(streamHandlers));
            context.AddSource("GeneratedStreamDispatcher.g.cs", SourceText.From(streamDispatcherSource, Encoding.UTF8));
        }
    }


    private static string GenerateExtensionClass(List<HandlerInfo> requestHandlers, List<HandlerInfo> streamHandlers)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Generated registration for the handlers discovered at compile time.</summary>");
        sb.AppendLine("    internal static class GeneratedMediatorExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Registers all discovered request and stream handlers as transient services, along with");
        sb.AppendLine("        /// the generated dispatchers (unless a dispatcher is already registered).");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static IServiceCollection AddGeneratedRequestHandlers(this IServiceCollection services)");
        sb.AppendLine("        {");

        foreach (var handler in requestHandlers)
        {
            var requestType = handler.RequestType;
            var handlerType = handler.HandlerType;

            if (handler.HasResponse)
            {
                var responseType = handler.ResponseType;
                sb.AppendLine($"            services.AddTransient<global::PutridParrot.Mediator.IRequestHandler<{requestType}, {responseType}>, {handlerType}>();");
            }
            else
            {
                sb.AppendLine($"            services.AddTransient<global::PutridParrot.Mediator.IRequestHandler<{requestType}>, {handlerType}>();");
            }
        }

        foreach (var handler in streamHandlers)
        {
            var requestType = handler.RequestType;
            var responseType = handler.ResponseType;
            var handlerType = handler.HandlerType;
            sb.AppendLine($"            services.AddTransient<global::PutridParrot.Mediator.IStreamRequestHandler<{requestType}, {responseType}>, {handlerType}>();");
        }

        if (requestHandlers.Count > 0)
        {
            sb.AppendLine("            services.TryAddSingleton<global::PutridParrot.Mediator.IRequestDispatcher, GeneratedRequestDispatcher>();");
        }

        if (streamHandlers.Count > 0)
        {
            sb.AppendLine("            services.TryAddSingleton<global::PutridParrot.Mediator.IStreamDispatcher, GeneratedStreamDispatcher>();");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a request dispatcher that uses direct method calls instead of reflection.
    /// This provides significantly better performance than reflection-based dispatch.
    /// </summary>
    /// <param name="handlers">The list of discovered request handlers.</param>
    /// <returns>A string containing the complete C# source code for the dispatcher class.</returns>
    /// <remarks>
    /// The generated dispatcher uses pattern matching and direct casts to invoke handlers
    /// without any reflection overhead. This can improve performance by 10-50x compared to reflection.
    /// </remarks>
    private static string GenerateDispatcher(List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
            
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PutridParrot.Mediator;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Generated request dispatcher that uses direct method calls for optimal performance.");
        sb.AppendLine("    /// This class is automatically generated at compile time and eliminates reflection overhead.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal sealed class GeneratedRequestDispatcher : IRequestDispatcher");
        sb.AppendLine("    {");
        AppendCanDispatch(sb, handlers);

        // Generate Dispatch<TResponse> method
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Dispatches a request with response using direct, strongly-typed method calls.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public Task<TResponse> Dispatch<TResponse>(");
        sb.AppendLine("            IRequest<TResponse> request,");
        sb.AppendLine("            object handler,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        
        var handlersWithResponse = handlers.Where(h => h.HasResponse).ToList();
        if (handlersWithResponse.Any())
        {
            sb.AppendLine("            return request switch");
            sb.AppendLine("            {");
            
            foreach (var handler in handlersWithResponse)
            {
                var requestType = handler.RequestType;
                var responseType = handler.ResponseType;
                // Cast to the specific handler type, then cast the result to TResponse
                sb.AppendLine($"                {requestType} r => (Task<TResponse>)(object)((global::PutridParrot.Mediator.IRequestHandler<{requestType}, {responseType}>)handler).Handle(r, cancellationToken),");
            }
            
            sb.AppendLine("                _ => throw new InvalidOperationException($\"No dispatcher case for request type {request.GetType().FullName}\")");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            throw new InvalidOperationException($\"No dispatcher case for request type {request.GetType().FullName}\");");
        }
        
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // Generate Dispatch method (no response)
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Dispatches a request without response using direct, strongly-typed method calls.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public Task Dispatch(");
        sb.AppendLine("            IRequest request,");
        sb.AppendLine("            object handler,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        
        var handlersWithoutResponse = handlers.Where(h => !h.HasResponse).ToList();
        if (handlersWithoutResponse.Any())
        {
            sb.AppendLine("            return request switch");
            sb.AppendLine("            {");
            
            foreach (var handler in handlersWithoutResponse)
            {
                var requestType = handler.RequestType;
                sb.AppendLine($"                {requestType} r => ((global::PutridParrot.Mediator.IRequestHandler<{requestType}>)handler).Handle(r, cancellationToken),");
            }
            
            sb.AppendLine("                _ => throw new InvalidOperationException($\"No dispatcher case for request type {request.GetType().FullName}\")");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            throw new InvalidOperationException($\"No dispatcher case for request type {request.GetType().FullName}\");");
        }
        
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a stream dispatcher that uses direct method calls instead of reflection.
    /// This provides significantly better performance than reflection-based dispatch for streaming requests.
    /// </summary>
    private static string GenerateStreamDispatcher(List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using PutridParrot.Mediator;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Generated stream dispatcher that uses direct method calls for optimal performance.");
        sb.AppendLine("    /// This class is automatically generated at compile time and eliminates reflection overhead.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal sealed class GeneratedStreamDispatcher : IStreamDispatcher");
        sb.AppendLine("    {");
        AppendCanDispatch(sb, handlers);
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Dispatches a streaming request using direct, strongly-typed method calls.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public IAsyncEnumerable<TResponse> DispatchStream<TResponse>(");
        sb.AppendLine("            IStreamRequest<TResponse> request,");
        sb.AppendLine("            object handler,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        
        if (handlers.Any())
        {
            sb.AppendLine("            return request switch");
            sb.AppendLine("            {");
            
            foreach (var handler in handlers)
            {
                var requestType = handler.RequestType;
                var responseType = handler.ResponseType;
                
                sb.AppendLine($"                {requestType} r => CastStream<{responseType}, TResponse>(");
                sb.AppendLine($"                    ((global::PutridParrot.Mediator.IStreamRequestHandler<{requestType}, {responseType}>)handler).Handle(r, cancellationToken)),");
            }
            
            sb.AppendLine("                _ => throw new InvalidOperationException($\"No stream dispatcher case for request type {request.GetType().FullName}\")");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            throw new InvalidOperationException($\"No stream dispatcher case for request type {request.GetType().FullName}\");");
        }
        
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Helper method to cast the async enumerable stream to the target type.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private static async IAsyncEnumerable<TTarget> CastStream<TSource, TTarget>(");
        sb.AppendLine("            IAsyncEnumerable<TSource> source)");
        sb.AppendLine("        {");
        sb.AppendLine("            await foreach (var item in source)");
        sb.AppendLine("            {");
        sb.AppendLine("                yield return (TTarget)(object)item!;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    // Fully qualified (global::) and keeping nullable annotations, e.g. IRequest<string?>
    private static readonly SymbolDisplayFormat TypeNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string Name(ITypeSymbol type) => type.ToDisplayString(TypeNameFormat);

    /// <summary>
    /// Emits the set of request types the dispatcher knows, so the mediator can fall back to
    /// reflection for handlers this generator did not see (e.g. in other assemblies).
    /// </summary>
    private static void AppendCanDispatch(StringBuilder sb, List<HandlerInfo> handlers)
    {
        sb.AppendLine("        private static readonly global::System.Collections.Generic.HashSet<Type> KnownRequestTypes = new()");
        sb.AppendLine("        {");
        foreach (var requestType in handlers.Select(h => h.RequestType).Distinct())
        {
            sb.AppendLine($"            typeof({requestType}),");
        }
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Returns true for the request types handled by this generated dispatcher.</summary>");
        sb.AppendLine("        public bool CanDispatch(Type requestType) => KnownRequestTypes.Contains(requestType);");
        sb.AppendLine();
    }

    private static void ReportDuplicateHandlers(SourceProductionContext context, List<HandlerInfo> handlers)
    {
        foreach (var group in handlers.GroupBy(h => h.RequestType).Where(g => g.Count() > 1))
        {
            var handlerNames = string.Join(", ", group.Select(h => h.HandlerType.Replace("global::", "")));
            context.ReportDiagnostic(Diagnostic.Create(DuplicateHandler, Location.None,
                group.Key.Replace("global::", ""), handlerNames));
        }
    }

    private static List<HandlerInfo> DistinctByRequestType(List<HandlerInfo> handlers) =>
        handlers.GroupBy(h => h.RequestType).Select(g => g.Last()).ToList();

    private static bool IsAccessibleFromAssembly(INamedTypeSymbol symbol)
    {
        for (var type = symbol; type != null; type = type.ContainingType)
        {
            if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;
        }
        return true;
    }

    private enum HandlerKind
    {
        Request,
        RequestNoResponse,
        Stream
    }

    /// <summary>
    /// Contains metadata about a discovered handler for code generation.
    /// A record so the incremental pipeline can compare results between runs.
    /// </summary>
    private sealed record HandlerInfo(HandlerKind Kind, string HandlerType, string RequestType, string ResponseType)
    {
        public bool HasResponse => Kind != HandlerKind.RequestNoResponse;
    }
}
