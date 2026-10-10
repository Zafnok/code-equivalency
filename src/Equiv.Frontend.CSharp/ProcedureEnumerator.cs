using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// Walks every named type in a <see cref="Compilation"/> and returns the procedures
/// ARCHITECTURE.md's frontend step 2 identifies: ordinary methods, constructors, property
/// get/set accessors, and operators, each with a body or <c>extern</c>. Local functions and lambdas never
/// appear as type members so they are excluded by construction; compiler-generated members
/// (<see cref="ISymbol.IsImplicitlyDeclared"/>) and abstract members, which name no implementation, are
/// excluded explicitly (ticket M2-002 acceptance criterion 1). An <c>extern</c> member has no body and is a
/// procedure all the same (ADR 0054; ticket P2-145): what it imports can change, and its own pair is where
/// that is reported. A partial method whose implementing part is <c>extern</c> is one, since Roslyn reports
/// its defining part as <c>extern</c>. A type the regex source generator wrote, with the types nested in it, holds no
/// procedure (ADR 0055 decision 4; ticket P2-118): it is how a <c>[GeneratedRegex]</c> member matches, and that member's
/// own pair stands for it. <see cref="CountEmitted"/> counts what is left out so.
/// </summary>
internal static class ProcedureEnumerator
{
    public static ImmutableArray<EnumeratedProcedure> Enumerate(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        return [.. Methods(compilation, emitted: false).Select(static method => new EnumeratedProcedure(RoslynIdentity.Of(method, RenameMap.Empty), method, method.Locations[0]))];
    }

    /// <summary>
    /// How many methods of <paramref name="compilation"/> would be procedures and are not, because a source generator
    /// emitted them as the implementation of a declaration that is compared in their place (ADR 0055 decision 4).
    /// </summary>
    public static int CountEmitted(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return Methods(compilation, emitted: true).Count();
    }

    private static IEnumerable<IMethodSymbol> Methods(Compilation compilation, bool emitted) =>
        TopLevelTypes(compilation.Assembly.GlobalNamespace)
            .Where(type => RegexGenerator.Emitted(type) == emitted)
            .SelectMany(AllNested)
            .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>().Where(IsIncluded));

    private static bool IsIncluded(IMethodSymbol method)
    {
        return !method.IsImplicitlyDeclared && !method.IsAbstract
            && method.MethodKind is MethodKind.Ordinary
            or MethodKind.Constructor
            or MethodKind.StaticConstructor
            or MethodKind.PropertyGet
            or MethodKind.PropertySet
            or MethodKind.UserDefinedOperator
            or MethodKind.Conversion;
    }

    private static IEnumerable<INamedTypeSymbol> TopLevelTypes(INamespaceSymbol @namespace) =>
        @namespace.GetTypeMembers().Concat(@namespace.GetNamespaceMembers().SelectMany(TopLevelTypes));

    private static IEnumerable<INamedTypeSymbol> AllNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            foreach (INamedTypeSymbol descendant in AllNested(nested))
            {
                yield return descendant;
            }
        }
    }
}
