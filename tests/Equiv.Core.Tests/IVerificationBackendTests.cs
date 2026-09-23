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
    }

    [Fact]
    public void ImplementationsCanBeInvokedThroughTheInterface()
    {
        IVerificationBackend backend = new AlwaysEquivalent();
        IrProcedure procedure = IrText.Parse("proc \"T::M()\" () entry B0 B0: ret");
        Verdict verdict = backend.Verify(procedure, procedure, new VerificationOptions(3, 5000, []));
        Assert.IsType<Equivalent>(verdict);
    }
}
