namespace Equiv.Core.Verdicts;

/// <summary>One failure-refinement query's <see cref="Outcome"/>, and for <see cref="RefinementOutcome.Found"/> the input and both replayed runs.</summary>
public sealed record RefinementResult(RefinementOutcome Outcome, Counterexample? Model = null)
{
    public static RefinementResult NoneProved { get; } = new(RefinementOutcome.NoneProved);

    public static RefinementResult Unknown { get; } = new(RefinementOutcome.Unknown);
}
