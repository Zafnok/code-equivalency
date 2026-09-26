namespace Equiv.Core.Execution;

/// <summary>
/// One Divergent's replay (ADR 0035 decision 2; ticket M4-009). <see cref="Reason"/> says why it is
/// <see cref="ReplayStatus.NotConstructible"/>, and is empty otherwise. <see cref="Legacy"/> and <see cref="Modern"/> are
/// both runtimes' outcomes of a <see cref="ReplayStatus.NotReproduced"/> replay, and null otherwise.
/// </summary>
public sealed record ReplayResult(ReplayStatus Status, string Reason, ExecutionOutcome? Legacy, ExecutionOutcome? Modern)
{
    public static ReplayResult Reproduced { get; } = new(ReplayStatus.Reproduced, string.Empty, Legacy: null, Modern: null);

    public static ReplayResult NotReproduced(ExecutionOutcome legacy, ExecutionOutcome modern) => new(ReplayStatus.NotReproduced, string.Empty, legacy, modern);

    public static ReplayResult NotConstructible(string reason) => new(ReplayStatus.NotConstructible, reason, Legacy: null, Modern: null);
}
