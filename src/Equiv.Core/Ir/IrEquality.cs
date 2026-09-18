using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Structural equality for the <see cref="ImmutableArray{T}"/> members of IR records
/// (<see cref="ImmutableArray{T}"/> itself compares by reference). Record <c>Equals</c>
/// overrides combine fields with non-short-circuit <c>&amp;</c> so that each is one branch.
/// </summary>
internal static class IrEquality
{
    public static bool SequenceEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
        left.AsSpan().SequenceEqual(right.AsSpan(), EqualityComparer<T>.Default);

    public static int Hash<T>(ImmutableArray<T> items)
    {
        HashCode hash = default;
        foreach (T item in items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
