using System.Collections.Immutable;

using Equiv.Core.Verdicts;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>What <see cref="ContractVerifier"/> concluded about a callee pair and a candidate contract (ticket P1-010).</summary>
internal abstract record ContractCheck
{
    private ContractCheck()
    {
    }

    /// <summary>The pair satisfies the contract on every input, as the rung <paramref name="By"/> proved.</summary>
    public sealed record Admitted(ProofMethod By) : ContractCheck;

    /// <summary>Some input makes the pair break the contract; <paramref name="Model"/> says which conjuncts it falsifies.</summary>
    public sealed record Rejected(ContractModel Model) : ContractCheck;

    /// <summary>Neither: <paramref name="Reason"/> says why.</summary>
    public sealed record Unknown(string Reason) : ContractCheck;
}
