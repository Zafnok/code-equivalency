namespace Equiv.Core.Execution;

/// <summary>
/// How to test one Unknown pair on generated inputs (ADR 0035 decision 3; ticket P1-008): the two drivers, the parameters
/// both sides take, position by position, and the seed cases, each in <see cref="ExecutionInput"/>'s wire form and given
/// to both sides alike. When the pair cannot be called with generated inputs, <see cref="Drivers"/> is null and
/// <see cref="Reason"/> says why.
/// </summary>
public sealed record TestingPlan(ExecutionDrivers? Drivers, IReadOnlyList<ExecutionParameter> Parameters, IReadOnlyList<ExecutionInput> Seeds, string Reason)
{
    public static TestingPlan Runnable(ExecutionDrivers drivers, IReadOnlyList<ExecutionParameter> parameters, IReadOnlyList<ExecutionInput> seeds) =>
        new(drivers, parameters, seeds, string.Empty);

    public static TestingPlan NotConstructible(string reason) => new(Drivers: null, [], [], reason);
}
