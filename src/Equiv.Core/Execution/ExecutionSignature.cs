namespace Equiv.Core.Execution;

/// <summary>
/// A member resolved on both runtimes: its identity, its parameters with an instance member's receiver first, and why no
/// input can be built for it. <see cref="NotConstructible"/> names each unsupported parameter type, or the member itself
/// when it cannot be called from generated source; the member is run only when it is empty.
/// </summary>
public sealed record ExecutionSignature(CallIdentity Member, IReadOnlyList<ExecutionParameter> Parameters, IReadOnlyList<string> NotConstructible);
