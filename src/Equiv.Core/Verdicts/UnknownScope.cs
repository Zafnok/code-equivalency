namespace Equiv.Core.Verdicts;

/// <summary>
/// How much of a method an <see cref="Unknown"/> leaves unverified (ADR 0029 decision 4; VERIFICATION-MODEL.md section 6).
/// A skipped project is not a result (its procedures are <c>unverified</c>), so there is no project scope.
/// </summary>
public enum UnknownScope
{
    /// <summary>
    /// Nothing is claimed: a whole-body opaque, a timeout, a loop or self-call the ladder did not decide, an unmatched
    /// overload, or unbound code.
    /// </summary>
    Method,

    /// <summary>
    /// Every cause is a construct inside the method, and ADR 0014's first query proved that no input reaching none of them
    /// diverges: the pair is equivalent unless a cause is reached (<see cref="Unknown.ResidualClaim"/>).
    /// </summary>
    Line,
}
