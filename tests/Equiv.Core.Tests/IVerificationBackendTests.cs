using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests;

/// <summary>The interface itself has no behaviour; this pins the stub signature ticket M3-001 builds against.</summary>
public sealed class IVerificationBackendTests
{
    private sealed class AlwaysEquivalent : IVerificationBackend
    {
        public Verdict Verify(ProcedurePair pair, VerificationOptions options) => new Equivalent();
    }

    [Fact]
    public void ImplementationsCanBeInvokedThroughTheInterface()
    {
        IVerificationBackend backend = new AlwaysEquivalent();
        ProcedureIdentity identity = new("T::M()");
        Verdict verdict = backend.Verify(new ProcedurePair(identity, identity), new VerificationOptions(3, 5000, []));
        Assert.IsType<Equivalent>(verdict);
    }
}
