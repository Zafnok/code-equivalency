using System.Collections.Immutable;

namespace Equiv.Core.Matching;

/// <summary>
/// Matches by exact <see cref="ProcedureIdentity"/> equality (VERIFICATION-MODEL.md section 4): an
/// identity present exactly once on each side pairs up; present on one side only is Added or Removed;
/// present more than once on a side that also has the identity is Ambiguous (an unmatched overload
/// group, reported as <see cref="Equiv.Core.Verdicts.UnknownReason.UnmatchedOverload"/>). Order in
/// the result follows first appearance across <paramref name="oldIdentities"/> then <paramref name="newIdentities"/>, not dictionary
/// iteration order, so results are reproducible.
/// </summary>
public sealed class StableIdentityMatcher : IProcedureMatcher
{
    public MatchResult Match(IReadOnlyCollection<ProcedureIdentity> oldIdentities, IReadOnlyCollection<ProcedureIdentity> newIdentities)
    {
        ArgumentNullException.ThrowIfNull(oldIdentities);
        ArgumentNullException.ThrowIfNull(newIdentities);

        Dictionary<ProcedureIdentity, int> oldCounts = CountBy(oldIdentities);
        Dictionary<ProcedureIdentity, int> newCounts = CountBy(newIdentities);

        HashSet<ProcedureIdentity> seen = [];
        List<ProcedureIdentity> order = [.. oldIdentities.Concat(newIdentities).Where(seen.Add)];

        ImmutableArray<ProcedurePair>.Builder pairs = ImmutableArray.CreateBuilder<ProcedurePair>();
        ImmutableArray<ProcedureIdentity>.Builder added = ImmutableArray.CreateBuilder<ProcedureIdentity>();
        ImmutableArray<ProcedureIdentity>.Builder removed = ImmutableArray.CreateBuilder<ProcedureIdentity>();
        ImmutableArray<ProcedureIdentity>.Builder ambiguous = ImmutableArray.CreateBuilder<ProcedureIdentity>();

        foreach (ProcedureIdentity identity in order)
        {
            int inOld = oldCounts.GetValueOrDefault(identity);
            int inNew = newCounts.GetValueOrDefault(identity);
            if (inOld == 0)
            {
                added.Add(identity);
            }
            else if (inNew == 0)
            {
                removed.Add(identity);
            }
            else if (inOld == 1 && inNew == 1)
            {
                pairs.Add(new ProcedurePair(identity, identity));
            }
            else
            {
                ambiguous.Add(identity);
            }
        }

        return new MatchResult(pairs.ToImmutable(), added.ToImmutable(), removed.ToImmutable(), ambiguous.ToImmutable());
    }

    private static Dictionary<ProcedureIdentity, int> CountBy(IReadOnlyCollection<ProcedureIdentity> identities)
    {
        Dictionary<ProcedureIdentity, int> counts = [];
        foreach (ProcedureIdentity identity in identities)
        {
            counts[identity] = counts.GetValueOrDefault(identity) + 1;
        }

        return counts;
    }
}
