using Equiv.Core.Execution;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Execute.Testing;

/// <summary>
/// What testing one Unknown pair gave (ADR 0035 decision 3; ticket P1-008): either <see cref="Testing"/>, the figures or
/// why none could be had, or <see cref="Observed"/>, a divergence the two real runtimes showed.
/// </summary>
public sealed record TestingOutcome(DifferentialTesting? Testing, ObservedDivergence? Observed)
{
    /// <summary>
    /// <paramref name="result"/> with this outcome: an observed divergence makes it Divergent with
    /// <c>proofMethod: observed</c>, and anything else leaves it Unknown with <see cref="VerificationResult.Testing"/>.
    /// Execution never proves, so nothing here yields Equivalent.
    /// </summary>
    public VerificationResult Apply(VerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Observed is { } observed ? result with { Verdict = Divergent.Observation(observed) } : result with { Testing = Testing };
    }
}
