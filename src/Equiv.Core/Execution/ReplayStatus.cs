namespace Equiv.Core.Execution;

/// <summary>
/// What replaying a Divergent's model on both real runtimes showed (ADR 0035 decision 2; ticket M4-009). SARIF
/// <c>properties.replay</c>. Replay never changes the verdict.
/// </summary>
public enum ReplayStatus
{
    /// <summary>The two runtimes' canonical outcomes differ on the model's inputs: the divergence is one the user can run.</summary>
    Reproduced,

    /// <summary>The two runtimes agree on the model's inputs: the model and the CLR disagree, a soundness or modelling finding.</summary>
    NotReproduced,

    /// <summary>The model's inputs could not be turned into a call on both runtimes, or a side gave no comparable outcome.</summary>
    NotConstructible,
}
