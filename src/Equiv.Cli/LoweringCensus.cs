using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Cli;

/// <summary>
/// What lowering produced, counted without a solver (ADR 0027; VERIFICATION-MODEL.md section 6). Written as
/// <c>run.properties.loweringCensus</c> on every run, and the whole point of <c>--lower-only</c>.
/// <see cref="Procedures"/> counts the procedures each side contributes to the match: matched pairs plus the
/// removed (legacy) or added (modern) ones. Every other count is per lowered body of a matched pair.
/// <see cref="OpaqueByReason"/> counts, per side, the bodies holding at least one <see cref="IrOpaque"/> with that
/// reason, so a whole-body opaque counts once under its reason. <see cref="PairsCongruent"/> stays 0 until
/// ticket M3-015.
/// </summary>
internal sealed record LoweringCensus(
    SideCounts Procedures,
    int MatchedPairs,
    int PairsWithoutOpaque,
    int PairsWholeBodyOpaque,
    int PairsCongruent,
    ImmutableSortedDictionary<string, SideCounts> OpaqueByReason)
{
    public static LoweringCensus Compute(IReadOnlyList<(IrProcedure Old, IrProcedure New)> pairs, int removed, int added)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        SortedDictionary<string, SideCounts> byReason = new(StringComparer.Ordinal);
        int withoutOpaque = 0;
        int wholeBodyOpaque = 0;
        foreach ((IrProcedure old, IrProcedure @new) in pairs)
        {
            ImmutableHashSet<string> oldReasons = Reasons(old);
            ImmutableHashSet<string> newReasons = Reasons(@new);
            withoutOpaque += oldReasons.IsEmpty && newReasons.IsEmpty ? 1 : 0;
            wholeBodyOpaque += IsWholeBodyOpaque(old) || IsWholeBodyOpaque(@new) ? 1 : 0;

            foreach (string reason in oldReasons.Union(newReasons))
            {
                SideCounts counts = byReason.GetValueOrDefault(reason, new SideCounts(0, 0));
                byReason[reason] = new SideCounts(
                    counts.Legacy + (oldReasons.Contains(reason) ? 1 : 0),
                    counts.Modern + (newReasons.Contains(reason) ? 1 : 0));
            }
        }

        return new LoweringCensus(
            new SideCounts(pairs.Count + removed, pairs.Count + added),
            pairs.Count,
            withoutOpaque,
            wholeBodyOpaque,
            PairsCongruent: 0,
            byReason.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    /// <summary>The census as the SARIF run property: camel-cased keys, <c>opaqueByReason</c> sorted by reason.</summary>
    public Dictionary<string, object> ToProperty() => new(StringComparer.Ordinal)
    {
        ["procedures"] = Property(Procedures),
        ["matchedPairs"] = MatchedPairs,
        ["pairsWithoutOpaque"] = PairsWithoutOpaque,
        ["pairsWholeBodyOpaque"] = PairsWholeBodyOpaque,
        ["pairsCongruent"] = PairsCongruent,
        ["opaqueByReason"] = new SortedDictionary<string, object>(
            OpaqueByReason.ToDictionary(static e => e.Key, static e => (object)Property(e.Value), StringComparer.Ordinal),
            StringComparer.Ordinal),
    };

    internal static Dictionary<string, object> Property(SideCounts counts) => new(StringComparer.Ordinal)
    {
        ["legacy"] = counts.Legacy,
        ["modern"] = counts.Modern,
    };

    private static ImmutableHashSet<string> Reasons(IrProcedure body) =>
        [.. body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Select(static o => o.Reason)];

    /// <summary>The shape a frontend gives a body it could not lower at all: one block whose only instruction is an <see cref="IrOpaque"/>.</summary>
    private static bool IsWholeBodyOpaque(IrProcedure body) => body.Blocks is [{ Instructions: [IrOpaque] }];
}
