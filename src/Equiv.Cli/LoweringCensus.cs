using System.Collections.Immutable;

using Equiv.Core;
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
/// changed unless it is congruent. <see cref="ExternalCallees"/> is every BCL member a lowered body calls, not only the
/// ones <see cref="RuntimeChangeTable"/> already lists (ADR 0035; ticket M3-033). <see cref="UnknownByScope"/> is set
/// only by a run that produced verdicts (ADR 0029 decision 4; ticket M3-025).
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
    RuntimeChangeCalls RuntimeChangeCalls,
    ExternalCallees ExternalCallees)
{
    public ScopeCounts? UnknownByScope { get; init; }

    public static LoweringCensus Compute(IReadOnlyList<(IrProcedure Old, IrProcedure New, bool Congruent)> pairs, int removed, int added, SideCounts? projectsSkipped = null, int unlowered = 0)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        Accumulator accumulator = new();
        foreach ((IrProcedure old, IrProcedure @new, bool isCongruent) in pairs)
        {
            accumulator.Add(old, @new, isCongruent);
        }

        return accumulator.Build(pairs.Count, removed, added, projectsSkipped ?? new SideCounts(0, 0), unlowered);
    }

    /// <summary>
    /// The census as the SARIF run property: camel-cased keys, <c>opaqueByReason</c> sorted by reason, and
    /// <c>unknownByScope</c> last, when set.
    /// </summary>
    public Dictionary<string, object> ToProperty()
    {
        Dictionary<string, object> property = Counts();
        if (UnknownByScope is { } scopes)
        {
            property["unknownByScope"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["line"] = scopes.Line,
                ["method"] = scopes.Method,
            };
        }

        return property;
    }

    private Dictionary<string, object> Counts() => new(StringComparer.Ordinal)
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
        ["externalCallees"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["legacy"] = ExternalCalleeList(ExternalCallees.Legacy),
            ["modern"] = ExternalCalleeList(ExternalCallees.Modern),
        },
    };

    internal static Dictionary<string, object> Property(SideCounts counts) => new(StringComparer.Ordinal)
    {
        ["legacy"] = counts.Legacy,
        ["modern"] = counts.Modern,
    };

    private static List<Dictionary<string, object>> ExternalCalleeList(ImmutableArray<ExternalCallee> callees) =>
        [.. callees.Select(static c => new Dictionary<string, object>(StringComparer.Ordinal) { ["member"] = c.Member, ["callSites"] = c.CallSites })];

    private static ImmutableHashSet<string> Reasons(IrProcedure body) =>
        [.. body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Select(static o => o.Reason)];

    /// <summary>The shape a frontend gives a body it could not lower at all: one block whose only instruction is an <see cref="IrOpaque"/>.</summary>
    private static bool IsWholeBodyOpaque(IrProcedure body) => body.Blocks is [{ Instructions: [IrOpaque] }];

    /// <summary>The running state <see cref="Compute"/> folds each pair into, kept off that method to stay under MA0051.</summary>
    private sealed class Accumulator
    {
        private readonly RuntimeChangeTable table = RuntimeChangeTable.Load();
        private readonly SortedDictionary<string, SideCounts> byReason = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> reasonSets = new(StringComparer.Ordinal);
        private readonly RuntimeChangeTally legacyCalls = new();
        private readonly RuntimeChangeTally modernCalls = new();
        private readonly ExternalCalleeTally legacyExternal = new();
        private readonly ExternalCalleeTally modernExternal = new();
        private int withoutOpaque;
        private int wholeBodyOpaque;
        private int changed;
        private int changedWithoutOpaque;
        private int changedWholeBodyOpaque;
        private int congruent;

        public void Add(IrProcedure old, IrProcedure @new, bool isCongruent)
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
            legacyExternal.Add(old);
            modernExternal.Add(@new);

            if (isCongruent)
            {
                congruent++;
                return;
            }

            changed++;
            changedWithoutOpaque += noOpaque;
            changedWholeBodyOpaque += wholeBody;
            string reasonSet = string.Join('+', oldReasons.Union(newReasons).Order(StringComparer.Ordinal));
            reasonSets[reasonSet] = reasonSets.GetValueOrDefault(reasonSet) + 1;
        }

        public LoweringCensus Build(int matched, int removed, int added, SideCounts projectsSkipped, int unlowered) => new(
            new SideCounts(matched + unlowered + removed, matched + unlowered + added),
            matched + unlowered,
            withoutOpaque,
            wholeBodyOpaque,
            congruent,
            projectsSkipped,
            byReason.ToImmutableSortedDictionary(StringComparer.Ordinal),
            new ChangedPairCounts(changed, changedWithoutOpaque, changedWholeBodyOpaque, reasonSets.ToImmutableSortedDictionary(StringComparer.Ordinal)),
            new RuntimeChangeCalls(
                new SideCounts(legacyCalls.CallSites, modernCalls.CallSites),
                new SideCounts(legacyCalls.Members.Count, modernCalls.Members.Count),
                new SideCounts(legacyCalls.Pairs, modernCalls.Pairs)),
            new ExternalCallees(legacyExternal.ToImmutableArray(), modernExternal.ToImmutableArray()));
    }

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

    /// <summary>One side's running <see cref="ExternalCallees"/> tally: call-site counts per distinct external callee.</summary>
    private sealed class ExternalCalleeTally
    {
        private readonly Dictionary<string, int> callSites = new(StringComparer.Ordinal);

        /// <summary>Counts <paramref name="body"/>'s calls whose <see cref="CallIdentity.External"/> is set.</summary>
        public void Add(IrProcedure body)
        {
            foreach (string member in body.Blocks
                .SelectMany(static b => b.Instructions)
                .OfType<IrCall>()
                .Where(static call => call.Callee.External)
                .Select(static call => call.Callee.Value))
            {
                callSites[member] = callSites.GetValueOrDefault(member) + 1;
            }
        }

        public ImmutableArray<ExternalCallee> ToImmutableArray() =>
            [.. callSites
                .Select(static e => new ExternalCallee(e.Key, e.Value))
                .OrderByDescending(static e => e.CallSites)
                .ThenBy(static e => e.Member, StringComparer.Ordinal)];
    }
}

