using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// Builds the <see cref="ProcedureIdentity"/> for a Roslyn <see cref="IMethodSymbol"/>
/// (ARCHITECTURE.md frontend step 2): <c>Namespace.Type::Member(ParamType1,ParamType2)</c> using
/// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> minus the <c>global::</c> prefix. The
/// declaring type and the method itself contribute generic arity as <c>`n</c> rather than their
/// literal type-parameter names, because an unbound generic parameter's spelling ("T" vs "TEntity")
/// is not assembly-agnostic; parameter types keep their literal <c>ref</c>/<c>out</c> prefix and
/// display string. Delegates to <see cref="ProcedureIdentityNormalizer.Member"/> (M1-003) so the
/// config's rename map applies identically for a frontend identity as for any other; the normaliser
/// itself is unchanged (CLAUDE.md).
/// </summary>
internal static class RoslynIdentity
{
    private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static ProcedureIdentity Of(IMethodSymbol symbol, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(renames);

        INamedTypeSymbol declaringType = symbol.ContainingType;
        string @namespace = declaringType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : StripGlobal(declaringType.ContainingNamespace.ToDisplayString(DisplayFormat));
        string typeName = TypeNameWithArity(declaringType);
        ImmutableArray<string> parameterTypes = [.. symbol.Parameters.Select(ParameterTypeName)];

        return ProcedureIdentityNormalizer.Member(@namespace, typeName, symbol.Name, symbol.Arity, parameterTypes, renames);
    }

    /// <summary>Dotted nested-type path, each level's own generic arity suffixed as <c>`n</c>.</summary>
    private static string TypeNameWithArity(INamedTypeSymbol type)
    {
        List<string> segments = [];
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            string arity = current.Arity > 0 ? $"`{current.Arity.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            segments.Insert(0, current.Name + arity);
        }

        return string.Join('.', segments);
    }

    private static string ParameterTypeName(IParameterSymbol parameter)
    {
        string prefix = parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            _ => string.Empty,
        };

        return prefix + StripGlobal(parameter.Type.ToDisplayString(DisplayFormat));
    }

    private static string StripGlobal(string displayName) =>
        displayName.StartsWith("global::", StringComparison.Ordinal) ? displayName["global::".Length..] : displayName;
}
