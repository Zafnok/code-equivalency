namespace Equiv.Core.Progress;

/// <summary>
/// What bounds a phase's time (ADR 0038): at most <paramref name="SolverItems"/> of its items reach the solver, and each
/// takes at most <paramref name="TimeoutMs"/> per rung for <paramref name="Rungs"/> rungs. The rest take no solver time.
/// </summary>
public sealed record PhaseBound(int SolverItems, int TimeoutMs, int Rungs);
