using System.Collections.Immutable;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// How a caller's product encodes a call to a callee it has a contract for (ADR 0036 decision 2; ticket P1-010). The only
/// production encoding is <see cref="FreshPerSideEncoding"/>. The seam exists so that a test can build the unsound
/// alternative, a shared function plus the contract, and show what it would prove.
/// </summary>
internal interface ICalleeContractEncoding
{
    /// <summary>
    /// Whether each side's call gets its own result, <c>threw</c> flag and heap functions, related to the other side's only by
    /// the contract. Sharing them would assume the two callees agree, which is exactly what is not known.
    /// </summary>
    bool FreshPerSide { get; }
}
