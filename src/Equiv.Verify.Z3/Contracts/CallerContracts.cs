using System.Collections.Immutable;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// The admitted contracts a caller's product uses (ADR 0036 decision 2; ticket P1-010), by canonical callee identity, and
/// how the product encodes a call to one of those callees (<see cref="Encoding"/>).
/// </summary>
internal sealed record CallerContracts(ImmutableDictionary<string, CalleeContract> Callees, ICalleeContractEncoding Encoding);
