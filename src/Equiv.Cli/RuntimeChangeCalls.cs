using Equiv.Core.RuntimeChanges;

namespace Equiv.Cli;

/// <summary>
/// Calls to a <see cref="RuntimeChangeTable"/> member over every matched pair, congruent ones included (ADR 0034): per
/// side, the call sites, the distinct callee identities among them, and the pairs whose body on that side has any.
/// </summary>
internal sealed record RuntimeChangeCalls(SideCounts CallSites, SideCounts DistinctMembers, SideCounts PairsWithAny);
