namespace Equiv.Core.Execution;

/// <summary>
/// One case's outcome on one runtime. <see cref="Canonical"/> is JSON text the driver writes with its own code, never with
/// a runtime's <c>ToString</c> (ADR 0035), so equal outcomes on the two runtimes are equal strings.
/// </summary>
public sealed record ExecutionOutcome(ExecutionInput Input, string Culture, OutcomeKind Kind, string Canonical);
