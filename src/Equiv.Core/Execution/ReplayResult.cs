namespace Equiv.Core.Execution;

/// <summary>
/// One Divergent's replay (ADR 0035 decision 2; ticket M4-009). <see cref="Reason"/> says why it is
/// <see cref="ReplayStatus.NotConstructible"/>, and is empty otherwise. <see cref="Legacy"/> and <see cref="Modern"/> are
/// both runtimes' outcomes of a <see cref="ReplayStatus.NotReproduced"/> or <see cref="ReplayStatus.NotApplicable"/>
/// replay, and null otherwise.
/// </summary>
public sealed record ReplayResult(ReplayStatus Status, string Reason, ExecutionOutcome? Legacy, ExecutionOutcome? Modern)
{
    /// <summary>
    /// How the drivers built each side's inputs (ADR 0062 decision 5; ticket P2-245): set on every replay whose drivers ran
    /// on inputs a factory built, and null on one that was not constructible before any input was built.
    /// </summary>
    public ReplayInputs? Inputs { get; init; }

    public static ReplayResult Reproduced { get; } = new(ReplayStatus.Reproduced, string.Empty, Legacy: null, Modern: null);

    public static ReplayResult NotReproduced(ExecutionOutcome legacy, ExecutionOutcome modern) => new(ReplayStatus.NotReproduced, string.Empty, legacy, modern);

    public static ReplayResult NotApplicable(ExecutionOutcome legacy, ExecutionOutcome modern) => new(ReplayStatus.NotApplicable, string.Empty, legacy, modern);

    public static ReplayResult NotConstructible(string reason) => new(ReplayStatus.NotConstructible, reason, Legacy: null, Modern: null);
}
