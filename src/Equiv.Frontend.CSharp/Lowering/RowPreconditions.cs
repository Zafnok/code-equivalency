using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The facts about a method that a <see cref="RuntimeChange.Requires"/> row needs (ticket P2-114). A row whose fact does not
/// hold of the method being lowered or fingerprinted is added to that method's list of suppressed rows, so none of its calls
/// is flagged runtime-changed; the lowering and the fingerprint both begin from <see cref="Suppress"/>'s list, and so agree.
/// </summary>
internal static class RowPreconditions
{
    private static readonly FrozenSet<string> AsyncStreamOperations =
        new[] { "ReadAsync", "WriteAsync", "BeginRead", "BeginWrite", "CopyToAsync", "FlushAsync" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="suppressed"/>, and the members of the rows whose <see cref="RowPrecondition"/> does not hold of a method
    /// whose operations are <paramref name="roots"/> (its body and, for a constructor, its initializers). A fact about a stream
    /// that came from a caller is not known here, so a method with no such call does not fire the row.
    /// </summary>
    public static ImmutableArray<string> Suppress(ImmutableArray<string> suppressed, IEnumerable<IOperation?> roots)
    {
        return CallsAsyncStreamOperation(roots)
            ? suppressed
            : [.. suppressed.Union(RuntimeChangeTable.Load().MembersRequiring(RowPrecondition.AsyncStreamOperation), StringComparer.Ordinal)];
    }

    private static bool CallsAsyncStreamOperation(IEnumerable<IOperation?> roots) =>
        roots.OfType<IOperation>().SelectMany(static root => root.DescendantsAndSelf()).OfType<IInvocationOperation>().Any(static call => IsAsyncStreamOperation(call.TargetMethod));

    private static bool IsAsyncStreamOperation(IMethodSymbol method) =>
        AsyncStreamOperations.Contains(method.Name) && DerivesFromStream(method.ContainingType);

    private static bool DerivesFromStream(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.Name, "Stream", StringComparison.Ordinal) && string.Equals(type.ContainingNamespace.ToDisplayString(), "System.IO", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
