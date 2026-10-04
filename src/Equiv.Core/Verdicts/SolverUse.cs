namespace Equiv.Core.Verdicts;

/// <summary>
/// A solver other than the backend's own that answered a query of a <see cref="LadderStep"/> (ADR 0050 decision 4;
/// ticket P1-033): its <see cref="ISmtSolver.Name"/> and <see cref="ISmtSolver.Version"/>.
/// </summary>
public sealed record SolverUse(string Name, string Version);
