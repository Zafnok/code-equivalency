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
/// <see cref="RuntimeChangeTable"/> (ticket M2-006) and is not listed in <c>equiv.config.json</c>'s
/// <c>suppressRuntimeChanges</c>; the flag is the backend's only input about it (ticket M3-001).
/// </summary>
internal static class CallIdentityFactory
{
    public static CallIdentity Of(IMethodSymbol method, RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges)
    {
        ArgumentNullException.ThrowIfNull(method);
        string identity = RoslynIdentity.Of(method, renames).Value;
        ImmutableArray<ITypeSymbol> typeArguments = [.. TypeArguments(method.ContainingType), .. method.TypeArguments];
        string value = typeArguments.IsEmpty
            ? identity
            : $"{identity}<{string.Join(',', typeArguments.Select(static t => t.ToDisplayString()))}>";
        CallIdentity callee = new(value);
        return callee with { RuntimeChanged = RuntimeChangeTable.Load().TryMatch(callee, suppressedRuntimeChanges, out _) };
    }

    private static IEnumerable<ITypeSymbol> TypeArguments(INamedTypeSymbol? type) =>
        type is null ? [] : TypeArguments(type.ContainingType).Concat(type.TypeArguments);
}
