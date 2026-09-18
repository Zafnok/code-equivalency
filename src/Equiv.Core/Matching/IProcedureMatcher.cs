namespace Equiv.Core.Matching;

/// <summary>
/// Pairs procedures across two solutions by stable identity (ARCHITECTURE.md; VERIFICATION-MODEL.md
/// section 4). Implementations must be total: every identity present in either input collection
/// appears in exactly one bucket of the returned <see cref="MatchResult"/>.
/// </summary>
public interface IProcedureMatcher
{
    MatchResult Match(IReadOnlyCollection<ProcedureIdentity> oldIdentities, IReadOnlyCollection<ProcedureIdentity> newIdentities);
}
