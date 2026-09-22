using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// Walks every named type in a <see cref="Compilation"/> and returns the procedures
/// ARCHITECTURE.md's frontend step 2 identifies: ordinary methods, constructors, property
/// get/set accessors, and operators, all with a body. Local functions and lambdas never appear as
/// type members so they are excluded by construction; compiler-generated members
/// (<see cref="ISymbol.IsImplicitlyDeclared"/>) and abstract/extern members (no body) are excluded
/// explicitly (ticket M2-002 acceptance criterion 1).
/// </summary>
internal static class ProcedureEnumerator
{
    public static ImmutableArray<EnumeratedProcedure> Enumerate(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        ImmutableArray<EnumeratedProcedure>.Builder result = ImmutableArray.CreateBuilder<EnumeratedProcedure>();
        foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(IsIncluded))
            {
                result.Add(new EnumeratedProcedure(RoslynIdentity.Of(method, RenameMap.Empty), method, method.Locations[0]));
            }
        }

        return result.ToImmutable();
    }

    private static bool IsIncluded(IMethodSymbol method)
    {
        return !method.IsImplicitlyDeclared && !method.IsAbstract && !method.IsExtern
            && method.MethodKind is MethodKind.Ordinary
            or MethodKind.Constructor
            or MethodKind.StaticConstructor
            or MethodKind.PropertyGet
            or MethodKind.PropertySet
            or MethodKind.UserDefinedOperator
            or MethodKind.Conversion;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol @namespace)
    {
        foreach (INamedTypeSymbol type in @namespace.GetTypeMembers())
        {
            foreach (INamedTypeSymbol nested in AllNested(type))
            {
                yield return nested;
            }
        }

        foreach (INamespaceSymbol child in @namespace.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in AllTypes(child))
            {
                yield return type;
            }
        }
    }

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
