using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace PutridParrot.Mediator.SourceGenerator;

/// <summary>
/// Source generator that discovers mediator handlers at compile time and generates reflection-free
/// registration and routing code for them.
/// </summary>
/// <remarks>
/// For each project it generates:
/// <list type="bullet">
/// <item><c>AddGeneratedMediatorHandlers()</c>, registering every handler, validator and exception handler
/// with strongly-typed calls.</item>
/// <item><c>GeneratedMediatorRoutes</c>, routing each request, command, stream and notification type to a
/// strongly-typed <c>MediatorRuntime</c> call.</item>
/// </list>
/// Together they make the mediator usable without reflection, including under trimming and Native AOT.
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

    // Fully qualified (global::) and keeping nullable annotations, e.g. IRequest<string?>, for generic arguments
    private static readonly SymbolDisplayFormat TypeNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // Fully qualified without nullable annotations or tuple element names, as typeof() requires
    private static readonly SymbolDisplayFormat TypeOfFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandValueTuple);

    /// <summary>
    /// Initializes the incremental generator pipeline.
    /// </summary>
    /// <param name="context">The incremental generator initialization context.</param>
    /// <remarks>
    /// Class declarations with a base list are filtered syntactically, then each is analyzed
    /// semantically for the mediator interfaces. The results are value-equatable so the IDE only
    /// regenerates when the set of handlers actually changes.
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
            !IsAccessible(classSymbol))
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
            if (!@interface.IsGenericType || !@interface.TypeArguments.All(IsAccessible))
                continue;

            var kind = @interface.ConstructedFrom.ToDisplayString() switch
            {
                "PutridParrot.Mediator.IRequestHandler<TRequest, TResponse>" => HandlerKind.Request,
                "PutridParrot.Mediator.IRequestHandler<TRequest>" => HandlerKind.Command,
                "PutridParrot.Mediator.IStreamRequestHandler<TRequest, TResponse>" => HandlerKind.Stream,
                "PutridParrot.Mediator.INotificationHandler<TNotification>" => HandlerKind.Notification,
                "PutridParrot.Mediator.IRequestValidator<TRequest>" => HandlerKind.Validator,
                "PutridParrot.Mediator.IRequestExceptionHandler<TRequest, TResponse>" => HandlerKind.ExceptionHandler,
                _ => (HandlerKind?)null
            };

            if (kind == null)
                continue;

            var request = @interface.TypeArguments[0];
            var response = @interface.TypeArguments.Length > 1 ? @interface.TypeArguments[1] : null;

            result.Add(new HandlerInfo(
                kind.Value,
                handlerType,
                Name(@interface),
                Name(request),
                TypeOf(request),
                response == null ? string.Empty : Name(response),
                response == null ? string.Empty : TypeOf(response)));
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Generates the registration method and the routes for all discovered handlers.
    /// </summary>
    private static void Execute(SourceProductionContext context, ImmutableArray<HandlerInfo> handlers)
    {
        if (handlers.IsEmpty)
            return;

        var requests = Routable(context, handlers, HandlerKind.Request);
        var commands = Routable(context, handlers, HandlerKind.Command);
        var streams = Routable(context, handlers, HandlerKind.Stream);

        // Several handlers per notification are normal; the route only needs each notification type once
        var notifications = handlers
            .Where(h => h.Kind == HandlerKind.Notification)
            .GroupBy(h => h.RequestTypeOf)
            .Select(g => g.First())
            .ToList();

        var hasRoutes = requests.Count + commands.Count + streams.Count + notifications.Count > 0;

        context.AddSource("MediatorExtensions.g.cs",
            SourceText.From(GenerateRegistration(handlers, hasRoutes), Encoding.UTF8));

        if (hasRoutes)
        {
            context.AddSource("GeneratedMediatorRoutes.g.cs",
                SourceText.From(GenerateRoutes(requests, commands, streams, notifications), Encoding.UTF8));
        }
    }

    /// <summary>
    /// The handlers of one kind with one entry per request type, reporting duplicates.
    /// </summary>
    private static List<HandlerInfo> Routable(SourceProductionContext context, ImmutableArray<HandlerInfo> handlers, HandlerKind kind)
    {
        var routable = new List<HandlerInfo>();

        foreach (var group in handlers.Where(h => h.Kind == kind).GroupBy(h => h.RequestTypeOf))
        {
            if (group.Count() > 1)
            {
                var handlerNames = string.Join(", ", group.Select(h => Display(h.HandlerType)));
                context.ReportDiagnostic(Diagnostic.Create(DuplicateHandler, Location.None, Display(group.Key), handlerNames));
            }

            // Matches dependency injection, where the last registration wins
            routable.Add(group.Last());
        }

        return routable;
    }

    private static string GenerateRegistration(ImmutableArray<HandlerInfo> handlers, bool hasRoutes)
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
        sb.AppendLine("        /// Registers every request, stream and notification handler, validator and exception handler in this");
        sb.AppendLine("        /// project as transient services (skipping any already registered), along with the project's generated routes. Uses no reflection, so it");
        sb.AppendLine("        /// is safe for trimming and Native AOT.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static IServiceCollection AddGeneratedMediatorHandlers(this IServiceCollection services)");
        sb.AppendLine("        {");

        foreach (var handler in handlers)
        {
            sb.AppendLine($"            services.TryAddEnumerable(ServiceDescriptor.Transient<{handler.ServiceType}, {handler.HandlerType}>());");
        }

        if (hasRoutes)
        {
            sb.AppendLine("            services.TryAddEnumerable(ServiceDescriptor.Singleton<global::PutridParrot.Mediator.IMediatorRoutes, GeneratedMediatorRoutes>());");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateRoutes(
        List<HandlerInfo> requests,
        List<HandlerInfo> commands,
        List<HandlerInfo> streams,
        List<HandlerInfo> notifications)
    {
        const string Runtime = "global::PutridParrot.Mediator.MediatorRuntime";
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace PutridParrot.Mediator");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Reflection-free routes from each request, command, stream and notification type in this project");
        sb.AppendLine("    /// to a strongly-typed MediatorRuntime call.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal sealed class GeneratedMediatorRoutes : global::PutridParrot.Mediator.IMediatorRoutes");
        sb.AppendLine("    {");

        AppendIndex(sb, "Requests", requests);
        AppendIndex(sb, "Commands", commands);
        AppendIndex(sb, "Streams", streams);
        AppendIndex(sb, "Notifications", notifications);

        // Requests: the response type check is constant-folded by the JIT
        AppendMethod(sb,
            "public Task<TResponse>? Send<TResponse>(global::PutridParrot.Mediator.IRequest<TResponse> request, global::PutridParrot.Mediator.MediatorContext context, CancellationToken cancellationToken)",
            "Requests", "request", requests,
            h => $"if (typeof(TResponse) == typeof({h.ResponseTypeOf})) return (Task<TResponse>)(object){Runtime}.Send<{h.RequestType}, {h.ResponseType}>(context, ({h.RequestType})(object)request, cancellationToken);");

        AppendMethod(sb,
            "public Task? Send(global::PutridParrot.Mediator.IRequest request, global::PutridParrot.Mediator.MediatorContext context, CancellationToken cancellationToken)",
            "Commands", "request", commands,
            h => $"return {Runtime}.SendCommand<{h.RequestType}>(context, ({h.RequestType})(object)request, cancellationToken);");

        AppendMethod(sb,
            "public IAsyncEnumerable<TResponse>? CreateStream<TResponse>(global::PutridParrot.Mediator.IStreamRequest<TResponse> request, global::PutridParrot.Mediator.MediatorContext context, CancellationToken cancellationToken)",
            "Streams", "request", streams,
            h => $"if (typeof(TResponse) == typeof({h.ResponseTypeOf})) return (IAsyncEnumerable<TResponse>)(object){Runtime}.CreateStream<{h.RequestType}, {h.ResponseType}>(context, ({h.RequestType})(object)request, cancellationToken);");

        AppendMethod(sb,
            "public Task? Publish(global::PutridParrot.Mediator.INotification notification, global::PutridParrot.Mediator.MediatorContext context, CancellationToken cancellationToken)",
            "Notifications", "notification", notifications,
            h => $"return {Runtime}.Publish<{h.RequestType}>(context, ({h.RequestType})(object)notification, cancellationToken);",
            last: true);

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Emits a map from each exact runtime type to its case index, so lookup is constant time.
    /// </summary>
    private static void AppendIndex(StringBuilder sb, string name, List<HandlerInfo> handlers)
    {
        if (handlers.Count == 0)
            return;

        sb.AppendLine($"        private static readonly Dictionary<Type, int> {name} = new()");
        sb.AppendLine("        {");
        for (var i = 0; i < handlers.Count; i++)
        {
            sb.AppendLine($"            [typeof({handlers[i].RequestTypeOf})] = {i},");
        }
        sb.AppendLine("        };");
        sb.AppendLine();
    }

    private static void AppendMethod(
        StringBuilder sb,
        string signature,
        string index,
        string parameter,
        List<HandlerInfo> handlers,
        System.Func<HandlerInfo, string> route,
        bool last = false)
    {
        sb.AppendLine($"        /// <summary>Routes known types; returns null for types this project has no handler for.</summary>");
        sb.AppendLine($"        {signature}");
        sb.AppendLine("        {");

        if (handlers.Count > 0)
        {
            sb.AppendLine($"            if (!{index}.TryGetValue({parameter}.GetType(), out var index)) return null;");
            sb.AppendLine("            switch (index)");
            sb.AppendLine("            {");
            for (var i = 0; i < handlers.Count; i++)
            {
                var statement = route(handlers[i]);
                sb.AppendLine($"                case {i}:");
                sb.AppendLine($"                    {statement}");

                // Conditional routes fall out of the switch when the response type doesn't match
                if (!statement.StartsWith("return "))
                {
                    sb.AppendLine("                    break;");
                }
            }
            sb.AppendLine("            }");
        }

        sb.AppendLine("            return null;");
        sb.AppendLine("        }");

        if (!last)
        {
            sb.AppendLine();
        }
    }

    private static string Name(ITypeSymbol type) => type.ToDisplayString(TypeNameFormat);

    private static string TypeOf(ITypeSymbol type) => type.ToDisplayString(TypeOfFormat);

    private static string Display(string name) => name.Replace("global::", "");

    /// <summary>
    /// Whether generated code in this assembly can refer to the type: no private or protected types
    /// anywhere in it (including generic arguments), and no type parameters.
    /// </summary>
    private static bool IsAccessible(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessible(array.ElementType);
            case ITypeParameterSymbol:
                return false;
            case INamedTypeSymbol named:
                for (var current = named; current != null; current = current.ContainingType)
                {
                    if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                        return false;
                }
                return named.TypeArguments.All(IsAccessible);
            default:
                return true;
        }
    }

    private enum HandlerKind
    {
        Request,
        Command,
        Stream,
        Notification,
        Validator,
        ExceptionHandler
    }

    /// <summary>
    /// Contains metadata about a discovered handler for code generation.
    /// A record so the incremental pipeline can compare results between runs.
    /// </summary>
    /// <param name="Kind">The kind of handler.</param>
    /// <param name="HandlerType">The handler class, fully qualified.</param>
    /// <param name="ServiceType">The implemented mediator interface, fully qualified.</param>
    /// <param name="RequestType">The request or notification type, for use as a generic argument.</param>
    /// <param name="RequestTypeOf">The request or notification type, for use in typeof().</param>
    /// <param name="ResponseType">The response type for use as a generic argument, or empty.</param>
    /// <param name="ResponseTypeOf">The response type for use in typeof(), or empty.</param>
    private sealed record HandlerInfo(
        HandlerKind Kind,
        string HandlerType,
        string ServiceType,
        string RequestType,
        string RequestTypeOf,
        string ResponseType,
        string ResponseTypeOf);
}
