namespace Equiv.Core.Verdicts;

/// <summary>
/// A solver other than the backend's own that answered a query of a <see cref="LadderStep"/> (ADR 0050 decision 4;
/// ticket P1-033): its <see cref="ISmtSolver.Name"/> and <see cref="ISmtSolver.Version"/>. <see cref="Partitions"/> is
/// how many partitions it was asked to split a query into when the answer kept came from them
/// (<see cref="ISmtSolver.AskInPartitions"/>; ticket P1-039), and null when every answer kept was a plain one.
/// </summary>
public sealed record SolverUse(string Name, string Version)
{
    public int? Partitions { get; init; }
}
