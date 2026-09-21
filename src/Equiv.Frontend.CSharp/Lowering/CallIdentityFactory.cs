using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The <see cref="CallIdentity"/> of an opaque callee: the M2-002 <see cref="RoslynIdentity"/> string
/// (rename map applied), plus <c>&lt;typeArgs&gt;</c> when the method or a containing type is a
/// constructed generic, so <c>F&lt;int&gt;()</c> and <c>F&lt;long&gt;()</c> are different functions.
/// <see cref="CallIdentity.RuntimeChanged"/> is set when the identity matches
/// <see cref="RuntimeChangeTable"/> (ticket M2-006); suppression from <c>equiv.config.json</c> is
/// applied later, once the config reaches the backend (M3-001).
/// </summary>
internal static class CallIdentityFactory
{
    public static CallIdentity Of(IMethodSymbol method, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(method);
        string identity = RoslynIdentity.Of(method, renames).Value;
        ImmutableArray<ITypeSymbol> typeArguments = [.. TypeArguments(method.ContainingType), .. method.TypeArguments];
        string value = typeArguments.IsEmpty
            ? identity
            : $"{identity}<{string.Join(",", typeArguments.Select(static t => t.ToDisplayString()))}>";
        CallIdentity callee = new(value);
        return callee with { RuntimeChanged = RuntimeChangeTable.Load().TryMatch(callee, out _) };
    }

    private static IEnumerable<ITypeSymbol> TypeArguments(INamedTypeSymbol? type) =>
        type is null ? [] : TypeArguments(type.ContainingType).Concat(type.TypeArguments);
}
