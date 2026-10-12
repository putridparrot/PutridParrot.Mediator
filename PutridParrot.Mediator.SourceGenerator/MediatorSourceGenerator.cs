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
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol classSymbol ||
            classSymbol.IsAbstract)
        {
            return ImmutableArray<HandlerInfo>.Empty;
        }

        // Partial classes have several declarations; only report the handler once
        if (classSymbol.DeclaringSyntaxReferences.Length > 1 &&
            classSymbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) != context.Node)
        {
            return ImmutableArray<HandlerInfo>.Empty;
        }

        var handlerType = classSymbol.ToDisplayString();
        var result = ImmutableArray.CreateBuilder<HandlerInfo>();

        foreach (var @interface in classSymbol.AllInterfaces)
        {
            if (!@interface.IsGenericType)
                continue;

            switch (@interface.ConstructedFrom.ToDisplayString())
            {
                case "PutridParrot.Mediator.IRequestHandler<TRequest, TResponse>":
                    result.Add(new HandlerInfo(HandlerKind.Request, handlerType,
                        @interface.TypeArguments[0].ToDisplayString(), @interface.TypeArguments[1].ToDisplayString()));
                    break;
                case "PutridParrot.Mediator.IRequestHandler<TRequest>":
                    result.Add(new HandlerInfo(HandlerKind.RequestNoResponse, handlerType,
                        @interface.TypeArguments[0].ToDisplayString(), string.Empty));
                    break;
                case "PutridParrot.Mediator.IStreamRequestHandler<TRequest, TResponse>":
                    result.Add(new HandlerInfo(HandlerKind.Stream, handlerType,
                        @interface.TypeArguments[0].ToDisplayString(), @interface.TypeArguments[1].ToDisplayString()));
                    break;
                // Note: IPipelineBehavior discovery removed - behaviors use runtime DI resolution
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

        if (requestHandlers.Count > 0)
        {
            var registrationSource = GenerateExtensionClass(requestHandlers);
            context.AddSource("MediatorExtensions.g.cs", SourceText.From(registrationSource, Encoding.UTF8));

            var dispatcherSource = GenerateDispatcher(requestHandlers);
            context.AddSource("GeneratedRequestDispatcher.g.cs", SourceText.From(dispatcherSource, Encoding.UTF8));
        }

        if (streamHandlers.Count > 0)
        {
            var streamDispatcherSource = GenerateStreamDispatcher(streamHandlers);
            context.AddSource("GeneratedStreamDispatcher.g.cs", SourceText.From(streamDispatcherSource, Encoding.UTF8));
        }
    }


    private static string GenerateExtensionClass(List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
            
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Generated registration for the request handlers discovered at compile time.</summary>");
        sb.AppendLine("    public static class GeneratedMediatorExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>Registers all discovered request handlers as transient services.</summary>");
        sb.AppendLine("        public static IServiceCollection AddGeneratedRequestHandlers(this IServiceCollection services)");
        sb.AppendLine("        {");

        foreach (var handler in handlers)
        {
            if (handler.HasResponse)
            {
                sb.AppendLine($"            services.AddTransient<PutridParrot.Mediator.IRequestHandler<{handler.RequestType}, {handler.ResponseType}>, {handler.HandlerType}>();");
            }
            else
            {
                sb.AppendLine($"            services.AddTransient<PutridParrot.Mediator.IRequestHandler<{handler.RequestType}>, {handler.HandlerType}>();");
            }
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
        sb.AppendLine("    public sealed class GeneratedRequestDispatcher : IRequestDispatcher");
        sb.AppendLine("    {");
        
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
                var requestType = GetFullyQualifiedTypeName(handler.RequestType);
                var responseType = GetFullyQualifiedTypeName(handler.ResponseType);
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
                var requestType = GetFullyQualifiedTypeName(handler.RequestType);
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
        sb.AppendLine("    public sealed class GeneratedStreamDispatcher : IStreamDispatcher");
        sb.AppendLine("    {");
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
                var requestType = GetFullyQualifiedTypeName(handler.RequestType);
                var responseType = GetFullyQualifiedTypeName(handler.ResponseType);
                
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

    private static string GetFullyQualifiedTypeName(string typeName)
    {
        // Handle primitive/built-in types - don't add global:: prefix
        var builtInTypes = new HashSet<string> 
        { 
            "int", "string", "bool", "byte", "sbyte", "short", "ushort", 
            "uint", "long", "ulong", "float", "double", "decimal", 
            "char", "object", "void"
        };
        
        if (builtInTypes.Contains(typeName))
        {
            return typeName;
        }
        
        if (typeName.StartsWith("<global namespace>"))
        {
            return $"global::{typeName.Replace("<global namespace>.", "")}";
        }
        
        // Check if it's already a qualified name with namespace
        if (typeName.Contains("."))
        {
            return $"global::{typeName}";
        }
        
        // It's a simple name in global namespace
        return $"global::{typeName}";
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
