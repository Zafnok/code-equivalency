using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests;

/// <summary>The interface itself has no behaviour; this pins the signature ticket M3-001 settled.</summary>
public sealed class IVerificationBackendTests
{
    private sealed class AlwaysEquivalent : IVerificationBackend
    {
        public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options) => new Equivalent(ProofMethod.Bounded);

        public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options) =>
            callees.IsEmpty ? null : new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse(callees[0].Identity, "true", "test")] };
    }

    [Fact]
    public void ImplementationsCanBeInvokedThroughTheInterface()
    {
        IVerificationBackend backend = new AlwaysEquivalent();
        IrProcedure procedure = IrText.Parse("proc \"T::M()\" () entry B0 B0: ret");
        Verdict verdict = backend.Verify(procedure, procedure, new VerificationOptions(3, 5000, []));
        Assert.IsType<Equivalent>(verdict);
        Assert.Null(backend.VerifyUnderContracts(procedure, procedure, [], new VerificationOptions(3, 5000, [])));
        CalleePair callee = new("T::F()", procedure, procedure);
        Assert.Equal("T::F()", Assert.Single(backend.VerifyUnderContracts(procedure, procedure, [callee], new VerificationOptions(3, 5000, []))!.ContractsUsed).Callee);
        Assert.Equal(procedure, callee.Old);
        Assert.Equal(procedure, callee.New);
    }
}
