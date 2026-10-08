using System.Collections.Immutable;

namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// One row of the API-equivalence catalogue (VERIFICATION-MODEL.md section 3; ADR 0020; ticket M3-009). A member entry
/// (<see cref="IsType"/> false) names a legacy and a modern member as exact <see cref="CallIdentity.Value"/>s and the
/// modern call's <see cref="Arguments"/> in terms of the legacy call's source arguments. A type entry names a legacy and a
/// modern type by metadata name and has no arguments. <see cref="Reason"/> says why the pair meets ADR 0020's soundness
/// condition, and <see cref="Url"/> is the Microsoft Learn page that establishes it.
/// </summary>
public sealed record ApiEquivalence(string Id, bool IsType, string Legacy, string Modern, ImmutableArray<ApiArgument> Arguments, string Reason, Uri Url)
{
    /// <summary>
    /// The first runtime that has the modern member, when an older one the catalogue is applied on lacks it (ticket
    /// P2-142). The entry then applies only to a pair whose <see cref="RuntimeInterval"/> crosses it: on any other pair
    /// the modern side cannot bind the modern member where the legacy side binds the legacy one, and a rewrite would
    /// only make the call a rebound one (ADR 0042). Null when every such runtime has it.
    /// </summary>
    public TargetRuntime? AddedIn { get; init; }

    /// <summary>Whether the entry applies to a pair that crosses <paramref name="interval"/>.</summary>
    public bool AppliesWithin(RuntimeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return AddedIn is not { } added || interval.Crosses(added);
    }
}
