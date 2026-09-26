namespace Equiv.Core.Execution;

/// <summary>
/// One BCL member to run on both real runtimes (ADR 0035, ticket M3-032): every input under every culture. A culture is a
/// <c>CultureInfo</c> name, or <c>invariant</c> for the invariant culture.
/// </summary>
public sealed record ExecutionRequest(CallIdentity Member, IReadOnlyList<ExecutionInput> Inputs, IReadOnlyList<string> Cultures);
