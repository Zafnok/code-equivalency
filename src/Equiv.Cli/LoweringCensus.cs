using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Core.RuntimeChanges;

namespace Equiv.Cli;

/// <summary>
/// What lowering produced, counted without a solver (ADR 0027; VERIFICATION-MODEL.md section 6). Written as
/// <c>run.properties.loweringCensus</c> on every run, and the whole point of <c>--lower-only</c>.
/// <see cref="Procedures"/> counts the procedures each side contributes to the match: matched pairs plus the
/// removed (legacy) or added (modern) ones. Every other count is per lowered body of a matched pair.
/// <see cref="OpaqueByReason"/> counts, per side, the bodies holding at least one <see cref="IrOpaque"/> with that
/// reason, so a whole-body opaque counts once under its reason. <see cref="PairsCongruent"/> counts the pairs that are
/// Equivalent by congruence (ADR 0024; ticket M3-015). <see cref="ProjectsSkipped"/> counts the projects each side's frontend skipped, in any language
/// (ADR 0029; ticket M3-024). A matched pair the frontend could not lower (ticket P2-011) counts in
/// <see cref="Procedures"/> and <see cref="MatchedPairs"/>, but has no body for any per-body count.
/// <see cref="Changed"/> and <see cref="RuntimeChangeCalls"/> are per matched pair (ADR 0034; ticket M3-030). A pair is
/// changed unless it is congruent.
/// </summary>
internal sealed record LoweringCensus(
    SideCounts Procedures,
    int MatchedPairs,
    int PairsWithoutOpaque,
    int PairsWholeBodyOpaque,
    int PairsCongruent,
    SideCounts ProjectsSkipped,
    ImmutableSortedDictionary<string, SideCounts> OpaqueByReason,
    ChangedPairCounts Changed,
    RuntimeChangeCalls RuntimeChangeCalls)
{
    public static LoweringCensus Compute(IReadOnlyList<(IrProcedure Old, IrProcedure New, bool Congruent)> pairs, int removed, int added, SideCounts? projectsSkipped = null, int unlowered = 0)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        RuntimeChangeTable table = RuntimeChangeTable.Load();
        SortedDictionary<string, SideCounts> byReason = new(StringComparer.Ordinal);
        SortedDictionary<string, int> reasonSets = new(StringComparer.Ordinal);
        int withoutOpaque = 0;
        int wholeBodyOpaque = 0;
        int changed = 0;
        int changedWithoutOpaque = 0;
        int changedWholeBodyOpaque = 0;
        int congruent = 0;
        RuntimeChangeTally legacyCalls = new();
        RuntimeChangeTally modernCalls = new();
        foreach ((IrProcedure old, IrProcedure @new, bool isCongruent) in pairs)
        {
            ImmutableHashSet<string> oldReasons = Reasons(old);
            ImmutableHashSet<string> newReasons = Reasons(@new);
            int noOpaque = oldReasons.IsEmpty && newReasons.IsEmpty ? 1 : 0;
            int wholeBody = IsWholeBodyOpaque(old) || IsWholeBodyOpaque(@new) ? 1 : 0;
            withoutOpaque += noOpaque;
            wholeBodyOpaque += wholeBody;

            foreach (string reason in oldReasons.Union(newReasons))
            {
                SideCounts counts = byReason.GetValueOrDefault(reason, new SideCounts(0, 0));
                byReason[reason] = new SideCounts(
                    counts.Legacy + (oldReasons.Contains(reason) ? 1 : 0),
                    counts.Modern + (newReasons.Contains(reason) ? 1 : 0));
            }

            legacyCalls.Add(old, table);
            modernCalls.Add(@new, table);

            if (isCongruent)
            {
                congruent++;
                continue;
            }

            changed++;
            changedWithoutOpaque += noOpaque;
            changedWholeBodyOpaque += wholeBody;
            string reasonSet = string.Join('+', oldReasons.Union(newReasons).Order(StringComparer.Ordinal));
            reasonSets[reasonSet] = reasonSets.GetValueOrDefault(reasonSet) + 1;
        }

        return new LoweringCensus(
            new SideCounts(pairs.Count + unlowered + removed, pairs.Count + unlowered + added),
            pairs.Count + unlowered,
            withoutOpaque,
            wholeBodyOpaque,
            congruent,
            projectsSkipped ?? new SideCounts(0, 0),
            byReason.ToImmutableSortedDictionary(StringComparer.Ordinal),
            new ChangedPairCounts(changed, changedWithoutOpaque, changedWholeBodyOpaque, reasonSets.ToImmutableSortedDictionary(StringComparer.Ordinal)),
            new RuntimeChangeCalls(
                new SideCounts(legacyCalls.CallSites, modernCalls.CallSites),
                new SideCounts(legacyCalls.Members.Count, modernCalls.Members.Count),
                new SideCounts(legacyCalls.Pairs, modernCalls.Pairs)));
    }

    /// <summary>The census as the SARIF run property: camel-cased keys, <c>opaqueByReason</c> sorted by reason.</summary>
    public Dictionary<string, object> ToProperty() => new(StringComparer.Ordinal)
    {
        ["procedures"] = Property(Procedures),
        ["matchedPairs"] = MatchedPairs,
        ["pairsWithoutOpaque"] = PairsWithoutOpaque,
        ["pairsWholeBodyOpaque"] = PairsWholeBodyOpaque,
        ["pairsCongruent"] = PairsCongruent,
        ["projectsSkipped"] = Property(ProjectsSkipped),
        ["opaqueByReason"] = new SortedDictionary<string, object>(
            OpaqueByReason.ToDictionary(static e => e.Key, static e => (object)Property(e.Value), StringComparer.Ordinal),
            StringComparer.Ordinal),
        ["changedPairs"] = Changed.Pairs,
        ["changedPairsWithoutOpaque"] = Changed.WithoutOpaque,
        ["changedPairsWholeBodyOpaque"] = Changed.WholeBodyOpaque,
        ["changedReasonSets"] = new SortedDictionary<string, int>(Changed.ReasonSets, StringComparer.Ordinal),
        ["runtimeChangeCalls"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["callSites"] = Property(RuntimeChangeCalls.CallSites),
            ["distinctMembers"] = Property(RuntimeChangeCalls.DistinctMembers),
            ["pairsWithAny"] = Property(RuntimeChangeCalls.PairsWithAny),
        },
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

    /// <summary>One side's running <see cref="RuntimeChangeCalls"/> counts.</summary>
    private sealed class RuntimeChangeTally
    {
        public int CallSites { get; private set; }

        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);

        public int Pairs { get; private set; }

        /// <summary>Counts <paramref name="body"/>'s calls that <paramref name="table"/> matches.</summary>
        public void Add(IrProcedure body, RuntimeChangeTable table)
        {
            string[] matched = [.. body.Blocks
                .SelectMany(static b => b.Instructions)
                .OfType<IrCall>()
                .Where(call => table.TryMatch(call.Callee, out _))
                .Select(static call => call.Callee.Value)];
            CallSites += matched.Length;
            Members.UnionWith(matched);
            Pairs += matched.Length > 0 ? 1 : 0;
        }
    }
}

