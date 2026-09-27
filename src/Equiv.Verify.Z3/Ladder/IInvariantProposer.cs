namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// Proposes a coupling invariant for rung 5 of the loop ladder (VERIFICATION-MODEL.md section 5.1; ticket P1-002). What
/// it returns is a hypothesis (ADR 0036): <see cref="LlmInvariantRung"/> admits it only once Z3 proves every rung 4
/// clause with it, so a wrong answer can never make a pair Equivalent.
/// </summary>
internal interface IInvariantProposer
{
    /// <summary>
    /// SMT-LIB text defining every relation of <paramref name="request"/> with <c>define-fun</c> over its arguments, or
    /// null to give up.
    /// </summary>
    Task<string?> ProposeAsync(InvariantRequest request, CancellationToken cancellationToken);
}
