using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Matching;

using Xunit;

namespace Equiv.Cli.Tests;

public sealed class NoBackendTests
{
    [Fact]
    public void NoBackend_Throws()
    {
        NoBackend backend = new();
        ProcedureIdentity identity = new("T::M()");
        VerificationOptions options = new(3, 5000, ImmutableDictionary<string, string>.Empty);

        Assert.Throws<InvalidOperationException>(() => backend.Verify(new ProcedurePair(identity, identity), options));
    }
}
