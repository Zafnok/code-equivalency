using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Core;

/// <summary>
/// Compares one matched procedure pair and returns a verdict (ARCHITECTURE.md; VERIFICATION-MODEL.md
/// section 1). <c>Equiv.Verify.Z3</c> implements it (ticket M3-001).
/// </summary>
public interface IVerificationBackend
{
    Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options);

    /// <summary>
    /// Re-verifies an Equivalent caller pair with a caller-sufficient contract in place of the shared function of each of
    /// <paramref name="callees"/> it can admit one for (ADR 0036 decision 2; ticket P1-010). Returns the caller's
    /// <see cref="Equivalent"/> with <see cref="Equivalent.ContractsUsed"/> naming the contracts, or null when no contract
    /// was admitted or the caller is not Equivalent under them.
    /// </summary>
    Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options);
}
