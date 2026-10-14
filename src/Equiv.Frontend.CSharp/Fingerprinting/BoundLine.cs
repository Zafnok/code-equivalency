using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Matching;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// One line of <see cref="BoundSerialiser"/>'s text, as the fields it is made of (ticket P2-173). An
/// <see cref="Operation"/>'s line has five: its kind, syntax kind and implicitness, its type, its constant, its symbols
/// and its context. Every other line declares something beside the operations (the signature, an attribute, a type's
/// layout) and is one field. The fields' values in order are the line, so two texts are equal exactly when their lines are.
/// </summary>
internal sealed record BoundLine(bool Operation, ImmutableArray<BoundField> Fields)
{
    /// <summary>
    /// The first component on which the texts made of <paramref name="old"/> and <paramref name="new"/> differ; null
    /// when they are equal. Where both sides have an operation's line, or both a declaration's, it is the first field
    /// whose values differ. Where one side has a declaration's line and the other an operation's or none, it is the
    /// declaration: a type that gained a field has one more line, and the operation the other side has there is not
    /// what changed.
    /// </summary>
    public static FingerprintDifference? First(ImmutableArray<BoundLine> old, ImmutableArray<BoundLine> @new) =>
        Enumerable.Range(0, Math.Max(old.Length, @new.Length))
            .Select(i => Differing(i < old.Length ? old[i] : null, i < @new.Length ? @new[i] : null))
            .OfType<FingerprintDifference>()
            .FirstOrDefault();

    private static FingerprintDifference? Differing(BoundLine? legacy, BoundLine? modern)
    {
        if (legacy is null || modern is null || legacy.Operation != modern.Operation)
        {
            // The sort is stable: a declaration's line ahead of an operation's, and the legacy side's ahead of the modern side's.
            BoundField named = ((BoundLine?[])[legacy, modern]).OfType<BoundLine>().OrderBy(static line => line.Operation).First().Fields[0];
            return new FingerprintDifference(named.Component, named.Identity);
        }

        return legacy.Fields.Zip(modern.Fields)
            .Where(static pair => !string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal))
            .Select(static pair => new FingerprintDifference(pair.First.Component, pair.First.Identity.Length > 0 ? pair.First.Identity : pair.Second.Identity))
            .FirstOrDefault();
    }
}
