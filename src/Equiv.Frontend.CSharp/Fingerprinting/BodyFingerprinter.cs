using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// A method's <see cref="BodyFingerprint"/> (ADR 0024; ticket M3-015): the SHA-256 of <see cref="BoundSerialiser"/>'s text for
/// the body of its first declaration, the one <c>IrLowerer</c> lowers. An instance constructor that does not chain to
/// <c>this(...)</c>, and a static constructor, also run their type's field and property initializers of the same staticness,
/// so those come first. An auto-accessor (one with a compiler-generated backing field) is fingerprinted as the body the compiler
/// generates for it. On a same-runtime pair, a partial method whose defining declaration is the one read is fingerprinted by
/// its implementing part, which holds its code, and by its attributes and those of that part's local functions
/// (ADR 0024 as clarified by ticket P2-107); an ordinary method only, since a partial constructor also runs its type's
/// initializers. Null when the declaration has no body otherwise.
/// </summary>
internal static class BodyFingerprinter
{
    /// <summary>
    /// <paramref name="method"/>'s fingerprint with <paramref name="config"/>'s rename map and runtime-change suppressions,
    /// and, on the <paramref name="legacy"/> side, its enabled API-equivalence entries (ADR 0020), as lowering uses them. It is
    /// runtime-sensitive by the rules that apply inside <paramref name="runtime"/>'s interval (ADR 0040; ticket P2-055).
    /// </summary>
    public static BodyFingerprint? Compute(IMethodSymbol method, Compilation compilation, EquivConfig config, bool legacy, SideRuntime runtime, ImmutableHashSet<string>? keptForwarders = null) =>
        Compute(method, compilation, config, legacy ? ApiEquivalenceTable.Load().Enabled(config.SuppressApiEquivalences) : [], runtime, keptForwarders);

    /// <summary>As the overload that takes the side, applying <paramref name="equivalences"/>, which only the legacy side has.</summary>
    public static BodyFingerprint? Compute(
        IMethodSymbol method, Compilation compilation, EquivConfig config, ImmutableArray<ApiEquivalence> equivalences, SideRuntime runtime, ImmutableHashSet<string>? keptForwarders = null) =>
        Text(method, compilation, config, equivalences, runtime, keptForwarders) is ({ } text, bool runtimeSensitive)
            ? new BodyFingerprint(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), runtimeSensitive)
            : null;

    /// <summary>The canonical serialisation the fingerprint hashes, readable for tests and review; a null text when there is no body.</summary>
    public static (string? Text, bool RuntimeSensitive) Text(
        IMethodSymbol method, Compilation compilation, EquivConfig config, ImmutableArray<ApiEquivalence> equivalences, SideRuntime runtime, ImmutableHashSet<string>? keptForwarders = null)
    {
        BoundSerialiser.Settings settings = new(config.Renames, config.SuppressRuntimeChanges, equivalences, runtime) { KeptForwarders = keptForwarders ?? [] };
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(config);

        SyntaxNode syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
        if (compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax) is not { } body)
        {
            return IsAutoAccessor(method)
                ? BoundSerialiser.Serialise(method, compilation, [], settings)
                : runtime.Interval.IsEmpty && method.MethodKind == MethodKind.Ordinary && method.PartialImplementationPart is { } implementation
                    ? ImplementingPart(implementation, compilation, settings)
                    : (null, false);
        }

        ImmutableArray<IOperation> operations = [.. Initializers(method, syntax).Select(node => compilation.GetSemanticModel(node.SyntaxTree).GetOperation(node)!), body];
        return BoundSerialiser.Serialise(method, compilation, operations, settings);
    }

    /// <summary>
    /// The text for a partial method whose defining declaration is the one read: its implementing part's, which holds the
    /// code. Null when that part has no body (it is <c>extern</c>) or does not bind (ADR 0029 decision 2).
    /// </summary>
    private static (string? Text, bool RuntimeSensitive) ImplementingPart(IMethodSymbol implementation, Compilation compilation, BoundSerialiser.Settings settings)
    {
        SyntaxNode syntax = implementation.DeclaringSyntaxReferences[0].GetSyntax();
        SemanticModel model = compilation.GetSemanticModel(syntax.SyntaxTree);
        return model.GetOperation(syntax) is { } body && IrLowerer.UnboundCauses(syntax, model, body).IsEmpty
            ? BoundSerialiser.SerialiseImplementingPart(implementation, body, compilation, settings)
            : (null, false);
    }

    /// <summary>An accessor of a property that has a compiler-generated backing field.</summary>
    private static bool IsAutoAccessor(IMethodSymbol method) =>
        method.AssociatedSymbol is IPropertySymbol property
        && property.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));

    /// <summary>The initializers a constructor runs ahead of its body, in declaration order.</summary>
    private static IEnumerable<SyntaxNode> Initializers(IMethodSymbol method, SyntaxNode syntax) =>
        method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
        && syntax is not ConstructorDeclarationSyntax { Initializer.RawKind: (int)SyntaxKind.ThisConstructorInitializer }
            ? method.ContainingType.GetMembers()
                .Where(member => member.IsStatic == method.IsStatic)
                .SelectMany(static member => member.DeclaringSyntaxReferences)
                .Select(static reference => reference.GetSyntax() switch
                {
                    VariableDeclaratorSyntax declarator => declarator.Initializer,
                    PropertyDeclarationSyntax property => property.Initializer,
                    _ => null,
                })
                .OfType<SyntaxNode>()
            : [];
}
