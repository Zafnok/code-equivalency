using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The <see cref="CallIdentity"/> of an opaque callee: the M2-002 <see cref="RoslynIdentity"/> string
/// (rename map applied), plus <c>&lt;typeArgs&gt;</c> when the method or a containing type is a
/// constructed generic, so <c>F&lt;int&gt;()</c> and <c>F&lt;long&gt;()</c> are different functions.
/// </summary>
internal static class CallIdentityFactory
{
    public static CallIdentity Of(IMethodSymbol method, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(method);
        string identity = RoslynIdentity.Of(method, renames).Value;
        ImmutableArray<ITypeSymbol> typeArguments = [.. TypeArguments(method.ContainingType), .. method.TypeArguments];
        return typeArguments.IsEmpty
            ? new CallIdentity(identity)
            : new CallIdentity($"{identity}<{string.Join(",", typeArguments.Select(static t => t.ToDisplayString()))}>");
    }

    private static IEnumerable<ITypeSymbol> TypeArguments(INamedTypeSymbol? type) =>
        type is null ? [] : TypeArguments(type.ContainingType).Concat(type.TypeArguments);
}
