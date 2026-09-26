using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Corpus.Seeder;

/// <summary>
/// A best-effort, syntax-only approximation of <c>Equiv.Core.Matching.ProcedureIdentityNormalizer</c>'s identity
/// string (ticket M4-010): <c>Namespace.Type::Method(paramType,...)</c>, from the syntax tree alone (no semantic
/// model, so parameter types are spelled as written, not fully qualified). It is not guaranteed to match the real
/// frontend's identity for a generic, aliased or unqualified type; the manifest also records the file and line so
/// <c>equiv-corpus-run</c>'s seeded mode can fall back to a location match (ADR 0028's own recall rule already keys
/// on "a cause on the seeded line", not on identity).
/// </summary>
internal static class MethodIdentity
{
    public static string Of(MethodDeclarationSyntax method)
    {
        string type = string.Join('.', DeclaringTypes(method));
        string @namespace = Namespace(method);
        string qualifiedType = @namespace.Length == 0 ? type : $"{@namespace}.{type}";
        int arity = method.TypeParameterList?.Parameters.Count ?? 0;
        string genericArity = arity > 0 ? $"`{arity.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;
        string parameters = string.Join(',', method.ParameterList.Parameters.Select(static p => p.Type?.ToString().Replace(" ", string.Empty, StringComparison.Ordinal) ?? "?"));
        return $"{qualifiedType}::{method.Identifier.Text}{genericArity}({parameters})";
    }

    /// <summary>Innermost type first is wrong for display; this returns outermost-to-innermost for a dotted name.</summary>
    private static List<string> DeclaringTypes(SyntaxNode node)
    {
        List<string> types = [];
        for (SyntaxNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is TypeDeclarationSyntax type)
            {
                types.Add(type.Identifier.Text);
            }
        }

        types.Reverse();
        return types;
    }

    private static string Namespace(SyntaxNode node)
    {
        List<string> parts = [];
        for (SyntaxNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BaseNamespaceDeclarationSyntax ns)
            {
                parts.Add(ns.Name.ToString());
            }
        }

        parts.Reverse();
        return string.Join('.', parts);
    }
}
