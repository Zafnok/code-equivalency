using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The <see cref="CallIdentity"/> of an opaque callee: the M2-002 <see cref="RoslynIdentity"/> string
/// (rename map applied), plus <c>&lt;typeArgs&gt;</c> when the method or a containing type is a
/// constructed generic, so <c>F&lt;int&gt;()</c> and <c>F&lt;long&gt;()</c> are different functions.
/// <see cref="CallIdentity.RuntimeChanged"/> is set when the identity matches
/// <see cref="RuntimeChangeTable"/> (ticket M2-006) and is not listed in <c>equiv.config.json</c>'s
/// <c>suppressRuntimeChanges</c>; the flag is the backend's only input about it (ticket M3-001). A legacy call an
/// API-equivalence entry rewrites (ticket M3-009) is checked against the table as the modern member it becomes.
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
        return Of(value, suppressedRuntimeChanges);
    }

    /// <summary>The identity <paramref name="value"/>, flagged runtime-changed as a callee with that identity would be.</summary>
    public static CallIdentity Of(string value, ImmutableArray<string> suppressedRuntimeChanges)
    {
        CallIdentity callee = new(value);
        return callee with { RuntimeChanged = RuntimeChangeTable.Load().TryMatch(callee, suppressedRuntimeChanges, out _) };
    }

    /// <summary>
    /// A call's source arguments as an API-equivalence adapter addresses them (ADR 0020; ticket M3-009), in evaluation
    /// order, each with its position: the receiver of an instance call is position 0, as the receiver of an extension
    /// call already is, and the arguments follow in parameter order with a <c>params</c> array's elements counted one by
    /// one. Null when a <c>params</c> parameter is given an array rather than elements, which the adapter cannot address.
    /// </summary>
    public static ImmutableArray<(int Position, IOperation Value)>? SourceArguments(IOperation? instance, ImmutableArray<IArgumentOperation> arguments)
    {
        Dictionary<IArgumentOperation, ImmutableArray<IOperation>> elements = [];
        foreach (IArgumentOperation argument in arguments)
        {
            if (Elements(argument) is not { } values)
            {
                return null;
            }

            elements[argument] = values;
        }

        Dictionary<IArgumentOperation, int> start = [];
        int next = instance is null ? 0 : 1;
        foreach (IArgumentOperation argument in arguments.OrderBy(static a => a.Parameter!.Ordinal))
        {
            start[argument] = next;
            next += elements[argument].Length;
        }

        IEnumerable<(int, IOperation)> receiver = instance is null ? [] : [(0, instance)];
        return [.. receiver.Concat(arguments.SelectMany(a => elements[a].Select((value, i) => (start[a] + i, value))))];
    }

    /// <summary>An argument's source values: a <c>params</c> array's elements, or the argument itself; null for an array passed to <c>params</c>.</summary>
    private static ImmutableArray<IOperation>? Elements(IArgumentOperation argument) => argument switch
    {
        { ArgumentKind: ArgumentKind.ParamArray, Value: IArrayCreationOperation { Initializer: { } initializer } } => initializer.ElementValues,
        { Parameter.IsParams: true } => null,
        _ => [argument.Value],
    };

    private static IEnumerable<ITypeSymbol> TypeArguments(INamedTypeSymbol? type) =>
        type is null ? [] : TypeArguments(type.ContainingType).Concat(type.TypeArguments);
}
