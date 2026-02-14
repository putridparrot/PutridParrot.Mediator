using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
public class MediatorSourceGenerator : ISourceGenerator
{
    /// <summary>
    /// Initializes the source generator and registers for syntax notifications.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    /// <remarks>
    /// Registers a <see cref="SyntaxReceiver"/> to collect class declarations during compilation.
    /// </remarks>
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    /// <summary>
    /// Executes the source generation logic to discover handlers and generate registration code.
    /// </summary>
    /// <param name="context">The generator execution context containing compilation information.</param>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="number">
    /// <item>Retrieves class declarations collected by the syntax receiver</item>
    /// <item>Analyzes each class to determine if it implements IRequestHandler interfaces</item>
    /// <item>Collects information about all discovered handlers</item>
    /// <item>Generates an extension method to register all handlers with dependency injection</item>
    /// </list>
    /// </para>
    /// <para>
    /// The generated code is added as "MediatorExtensions.g.cs" to the compilation.
    /// </para>
    /// </remarks>
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
            return;

        var requestHandlers = new List<HandlerInfo>();
        var pipelineBehaviors = new List<BehaviorInfo>();

        foreach (var candidate in receiver.Candidates)
        {
            var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
            var classSymbol = model.GetDeclaredSymbol(candidate) as INamedTypeSymbol;

            if (classSymbol == null)
                continue;

            foreach (var @interface in classSymbol.AllInterfaces)
            {
                if (@interface.IsGenericType)
                {
                    var typeName = @interface.ConstructedFrom.ToDisplayString();
                        
                    if (typeName == "PutridParrot.Mediator.IRequestHandler<TRequest, TResponse>")
                    {
                        var requestType = @interface.TypeArguments[0];
                        var responseType = @interface.TypeArguments[1];
                            
                        requestHandlers.Add(new HandlerInfo
                        {
                            HandlerType = classSymbol.ToDisplayString(),
                            RequestType = requestType.ToDisplayString(),
                            ResponseType = responseType.ToDisplayString(),
                            HasResponse = true
                        });
                    }
                    else if (typeName == "PutridParrot.Mediator.IRequestHandler<TRequest>")
                    {
                        var requestType = @interface.TypeArguments[0];
                            
                        requestHandlers.Add(new HandlerInfo
                        {
                            HandlerType = classSymbol.ToDisplayString(),
                            RequestType = requestType.ToDisplayString(),
                            ResponseType = null,
                            HasResponse = false
                        });
                    }
                    else if (typeName == "PutridParrot.Mediator.IPipelineBehavior<TRequest, TResponse>")
                    {
                        var requestType = @interface.TypeArguments[0];
                        var responseType = @interface.TypeArguments[1];
                            
                        pipelineBehaviors.Add(new BehaviorInfo
                        {
                            BehaviorType = classSymbol.ToDisplayString(),
                            RequestType = requestType.ToDisplayString(),
                            ResponseType = responseType.ToDisplayString()
                        });
                    }
                }
            }
        }

        if (requestHandlers.Count > 0)
        {
            var registrationSource = GenerateExtensionClass(requestHandlers);
            context.AddSource("MediatorExtensions.g.cs", SourceText.From(registrationSource, Encoding.UTF8));
            
            var dispatcherSource = GenerateDispatcher(requestHandlers);
            context.AddSource("GeneratedRequestDispatcher.g.cs", SourceText.From(dispatcherSource, Encoding.UTF8));
            
            // Always generate behavior pipeline - it will resolve behaviors at runtime
            var behaviorPipelineSource = GenerateBehaviorPipelineSimple();
            context.AddSource("GeneratedBehaviorPipeline.g.cs", SourceText.From(behaviorPipelineSource, Encoding.UTF8));
        }
    }

    private string GenerateExtensionClass(List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
            
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedMediatorExtensions");
        sb.AppendLine("    {");
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
    private string GenerateDispatcher(List<HandlerInfo> handlers)
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

    private string GetFullyQualifiedTypeName(string typeName)
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

    /// <summary>
    /// Generates a simple behavior pipeline that resolves behaviors at runtime.
    /// Supports both open generic and closed generic behaviors.
    /// </summary>
    private string GenerateBehaviorPipelineSimple()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using PutridParrot.Mediator;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Generated behavior pipeline for automatic behavior execution.");
        sb.AppendLine("    /// Supports both open generic and closed generic behaviors.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public sealed class GeneratedBehaviorPipeline");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IServiceProvider _serviceProvider;");
        sb.AppendLine();
        sb.AppendLine("        public GeneratedBehaviorPipeline(IServiceProvider serviceProvider)");
        sb.AppendLine("        {");
        sb.AppendLine("            _serviceProvider = serviceProvider;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public async Task<TResponse> ExecuteWithBehaviors<TRequest, TResponse>(");
        sb.AppendLine("            TRequest request,");
        sb.AppendLine("            Func<Task<TResponse>> handlerDelegate,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("            where TRequest : IRequest<TResponse>");
        sb.AppendLine("        {");
        sb.AppendLine("            // Get all registered behaviors for this request/response type");
        sb.AppendLine("            var behaviors = _serviceProvider.GetServices<global::PutridParrot.Mediator.IPipelineBehavior<TRequest, TResponse>>();");
        sb.AppendLine();
        sb.AppendLine("            if (!behaviors.Any())");
        sb.AppendLine("            {");
        sb.AppendLine("                return await handlerDelegate();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            // Build pipeline by wrapping each behavior");
        sb.AppendLine("            RequestHandlerDelegate<TResponse> pipeline = new RequestHandlerDelegate<TResponse>(handlerDelegate);");
        sb.AppendLine();
        sb.AppendLine("            foreach (var behavior in behaviors.Reverse())");
        sb.AppendLine("            {");
        sb.AppendLine("                var currentPipeline = pipeline;");
        sb.AppendLine("                pipeline = new RequestHandlerDelegate<TResponse>(() => behavior.Handle(request, currentPipeline, cancellationToken));");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return await pipeline();");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Contains metadata about a discovered request handler for code generation.
    /// </summary>
    private class HandlerInfo
    {
        public string HandlerType { get; set; }
        public string RequestType { get; set; }
        public string ResponseType { get; set; }
        public bool HasResponse { get; set; }
    }

    /// <summary>
    /// Contains metadata about a discovered pipeline behavior for code generation.
    /// </summary>
    private class BehaviorInfo
    {
        public string BehaviorType { get; set; }
        public string RequestType { get; set; }
        public string ResponseType { get; set; }
    }

    /// <summary>
    /// Generates a behavior pipeline builder that automatically wires up behaviors at compile time.
    /// </summary>
    private string GenerateBehaviorPipeline(List<BehaviorInfo> behaviors, List<HandlerInfo> handlers)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using PutridParrot.Mediator;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Generated behavior pipeline builder for automatic behavior discovery and execution.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public sealed class GeneratedBehaviorPipeline");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IServiceProvider _serviceProvider;");
        sb.AppendLine();
        sb.AppendLine("        public GeneratedBehaviorPipeline(IServiceProvider serviceProvider)");
        sb.AppendLine("        {");
        sb.AppendLine("            _serviceProvider = serviceProvider;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // Generate BuildPipeline method for requests with responses
        sb.AppendLine("        public async Task<TResponse> ExecuteWithBehaviors<TRequest, TResponse>(");
        sb.AppendLine("            TRequest request,");
        sb.AppendLine("            Func<Task<TResponse>> handlerDelegate,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("            where TRequest : IRequest<TResponse>");
        sb.AppendLine("        {");
        sb.AppendLine("            var requestType = typeof(TRequest);");
        sb.AppendLine();
        
        // Generate switch statement for each request type that has behaviors
        var requestsWithBehaviors = behaviors
            .Where(b => handlers.Any(h => h.HasResponse && h.RequestType == b.RequestType))
            .GroupBy(b => b.RequestType)
            .ToList();
        
        if (requestsWithBehaviors.Any())
        {
            sb.AppendLine("            // Execute behaviors for specific request types");
            sb.AppendLine("            return requestType.FullName switch");
            sb.AppendLine("            {");
            
            foreach (var group in requestsWithBehaviors)
            {
                var requestType = GetFullyQualifiedTypeName(group.Key);
                var responseType = GetFullyQualifiedTypeName(group.First().ResponseType);
                
                sb.AppendLine($"                \"{group.Key}\" => await Execute{SanitizeTypeName(group.Key)}_WithBehaviors(");
                sb.AppendLine($"                    ({requestType})((object)request),");
                sb.AppendLine($"                    handlerDelegate,");
                sb.AppendLine($"                    cancellationToken),");
            }
            
            sb.AppendLine("                _ => await handlerDelegate() // No behaviors for this request type");
            sb.AppendLine("            };");
        }
        else
        {
            sb.AppendLine("            // No behaviors registered, execute handler directly");
            sb.AppendLine("            return await handlerDelegate();");
        }
        
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // Generate specific pipeline methods for each request type
        foreach (var group in requestsWithBehaviors)
        {
            var requestType = GetFullyQualifiedTypeName(group.Key);
            var responseType = GetFullyQualifiedTypeName(group.First().ResponseType);
            var methodName = $"Execute{SanitizeTypeName(group.Key)}_WithBehaviors";
            
            sb.AppendLine($"        private async Task<{responseType}> {methodName}(");
            sb.AppendLine($"            {requestType} request,");
            sb.AppendLine($"            Func<Task<{responseType}>> handlerDelegate,");
            sb.AppendLine($"            CancellationToken cancellationToken)");
            sb.AppendLine("        {");
            sb.AppendLine($"            RequestHandlerDelegate<{responseType}> pipeline = handlerDelegate;");
            sb.AppendLine();
            
            // Reverse order so behaviors wrap correctly
            foreach (var behavior in group.Reverse())
            {
                var behaviorRequestType = GetFullyQualifiedTypeName(behavior.RequestType);
                var behaviorResponseType = GetFullyQualifiedTypeName(behavior.ResponseType);
                var behaviorInterfaceType = $"global::PutridParrot.Mediator.IPipelineBehavior<{behaviorRequestType}, {behaviorResponseType}>";
                
                sb.AppendLine($"            // Add {behavior.BehaviorType}");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                var behavior = _serviceProvider.GetService<{behaviorInterfaceType}>();");
                sb.AppendLine($"                if (behavior != null)");
                sb.AppendLine($"                {{");
                sb.AppendLine($"                    var currentPipeline = pipeline;");
                sb.AppendLine($"                    pipeline = () => behavior.Handle(request, currentPipeline, cancellationToken);");
                sb.AppendLine($"                }}");
                sb.AppendLine($"            }}");
                sb.AppendLine();
            }
            
            sb.AppendLine("            return await pipeline();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        sb.AppendLine("    }");
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    private string SanitizeTypeName(string typeName)
    {
        // Remove namespace and special characters for method names
        var sanitized = typeName
            .Replace("<global namespace>.", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");
        
        return sanitized;
    }

    private class SyntaxReceiver : ISyntaxContextReceiver
    {
        public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is ClassDeclarationSyntax classDeclaration &&
                classDeclaration.BaseList != null)
            {
                Candidates.Add(classDeclaration);
            }
        }
    }
}