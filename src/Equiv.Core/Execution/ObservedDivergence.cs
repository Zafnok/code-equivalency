namespace Equiv.Core.Execution;

/// <summary>
/// An input on which the two real runtimes' canonical outcomes differed while an Unknown pair was tested (ADR 0035
/// decision 3; ticket P1-008). Both outcomes carry the input and the culture. It makes the result EQ002 with
/// <c>proofMethod: observed</c>.
/// </summary>
public sealed record ObservedDivergence(ExecutionOutcome Legacy, ExecutionOutcome Modern);
