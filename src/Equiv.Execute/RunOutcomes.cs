using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>Every case's outcome from each of the two runs of each side, in the same case order.</summary>
internal sealed record RunOutcomes(
    IReadOnlyList<ExecutionOutcome> Legacy1,
    IReadOnlyList<ExecutionOutcome> Legacy2,
    IReadOnlyList<ExecutionOutcome> Modern1,
    IReadOnlyList<ExecutionOutcome> Modern2);
