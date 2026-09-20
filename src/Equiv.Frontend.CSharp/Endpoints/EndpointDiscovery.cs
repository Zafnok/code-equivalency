using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Endpoints;

/// <summary>
/// Maps Web API 2 / MVC 5 / ASP.NET Core attribute routing to <see cref="Endpoint"/>s (ARCHITECTURE.md
/// frontend step 3; ticket M2-005 acceptance criterion 1). Detection is by attribute metadata name only
/// (never by base class, so a fake attribute of the same name works fine in a unit test): a closed set
/// of route/prefix attributes and a closed verb-attribute-to-HTTP-verb map. Controllers are never nested
/// types, unlike the general procedures <see cref="Equiv.Frontend.CSharp.ProcedureEnumerator"/> walks,
/// so only namespace-scoped types are considered. An action with neither a route attribute nor a verb
/// attribute of its own is skipped (criterion 2: convention-based routing is out of scope).
/// </summary>
internal static class EndpointDiscovery
{
    private static readonly ImmutableHashSet<string> PrefixAttributes = ["System.Web.Http.RoutePrefixAttribute"];

    private static readonly ImmutableHashSet<string> RouteAttributes =
    [
        "System.Web.Http.RouteAttribute",
        "System.Web.Mvc.RouteAttribute",
        "Microsoft.AspNetCore.Mvc.RouteAttribute",
    ];

    private static readonly ImmutableDictionary<string, string> VerbAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["System.Web.Http.HttpGetAttribute"] = "GET",
        ["System.Web.Http.HttpPostAttribute"] = "POST",
        ["System.Web.Http.HttpPutAttribute"] = "PUT",
        ["System.Web.Http.HttpDeleteAttribute"] = "DELETE",
        ["System.Web.Http.HttpPatchAttribute"] = "PATCH",
        ["System.Web.Mvc.HttpGetAttribute"] = "GET",
        ["System.Web.Mvc.HttpPostAttribute"] = "POST",
        ["System.Web.Mvc.HttpPutAttribute"] = "PUT",
        ["System.Web.Mvc.HttpDeleteAttribute"] = "DELETE",
        ["System.Web.Mvc.HttpPatchAttribute"] = "PATCH",
        ["Microsoft.AspNetCore.Mvc.HttpGetAttribute"] = "GET",
        ["Microsoft.AspNetCore.Mvc.HttpPostAttribute"] = "POST",
        ["Microsoft.AspNetCore.Mvc.HttpPutAttribute"] = "PUT",
        ["Microsoft.AspNetCore.Mvc.HttpDeleteAttribute"] = "DELETE",
        ["Microsoft.AspNetCore.Mvc.HttpPatchAttribute"] = "PATCH",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    public static ImmutableArray<Endpoint> Discover(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        ImmutableArray<Endpoint>.Builder result = ImmutableArray.CreateBuilder<Endpoint>();
        foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            string? prefix = TypePrefix(type);
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(IsCandidate))
            {
                if (DiscoverAction(method, prefix) is { } endpoint)
                {
                    result.Add(endpoint);
                }
            }
        }

        return result.ToImmutable();
    }

    private static Endpoint? DiscoverAction(IMethodSymbol method, string? prefix)
    {
        string? methodRoute = null;
        string? verb = null;
        string? verbInlineTemplate = null;

        foreach (AttributeData attribute in method.GetAttributes())
        {
            string name = AttributeName(attribute);
            if (RouteAttributes.Contains(name))
            {
                methodRoute = FirstArgument(attribute);
            }
            else if (VerbAttributes.TryGetValue(name, out string? mappedVerb))
            {
                verb = mappedVerb;
                verbInlineTemplate = FirstArgument(attribute);
            }
        }

        if (methodRoute is null && verb is null)
        {
            return null;
        }

        string template = RouteTemplate.Normalize(prefix, methodRoute ?? verbInlineTemplate, method.ContainingType.Name, method.Name);
        return new Endpoint(verb ?? "GET", template, RoslynIdentity.Of(method, RenameMap.Empty));
    }

    private static string? TypePrefix(INamedTypeSymbol type)
    {
        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        AttributeData? prefixAttribute = attributes.FirstOrDefault(a => PrefixAttributes.Contains(AttributeName(a)))
            ?? attributes.FirstOrDefault(a => RouteAttributes.Contains(AttributeName(a)));
        return prefixAttribute is null ? null : FirstArgument(prefixAttribute);
    }

    private static bool IsCandidate(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared && !method.IsAbstract && !method.IsExtern;

    /// <summary>Every attribute usage in a successfully compiled program resolves to a real class (never null).</summary>
    private static string AttributeName(AttributeData attribute) =>
        StripGlobal(attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static string? FirstArgument(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 0 ? null : attribute.ConstructorArguments[0].Value as string;

    private static string StripGlobal(string displayName) =>
        displayName.StartsWith("global::", StringComparison.Ordinal) ? displayName["global::".Length..] : displayName;

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol @namespace)
    {
        foreach (INamedTypeSymbol type in @namespace.GetTypeMembers())
        {
            yield return type;
        }

        foreach (INamespaceSymbol child in @namespace.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in AllTypes(child))
            {
                yield return type;
            }
        }
    }
}
