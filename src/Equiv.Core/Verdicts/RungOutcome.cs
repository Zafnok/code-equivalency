namespace Equiv.Core.Verdicts;

/// <summary>What one rung of the loop ladder concluded (a <see cref="LadderStep"/>).</summary>
public enum RungOutcome
{
    /// <summary>The rung proved the pair equivalent.</summary>
    Proved,

    /// <summary>The rung found a counterexample that replays to a divergence.</summary>
    Refuted,

    /// <summary>The rung ran and neither proved nor refuted the pair.</summary>
    Inconclusive,

    /// <summary>The solver returned unknown on one of the rung's queries.</summary>
    Timeout,

    /// <summary>The rung does not apply to the pair's shape, so it did not run.</summary>
    NotApplicable,
}
