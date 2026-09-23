using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

public sealed class ResultFingerprintTests
{
    [Fact]
    public void SameIdentityAndVerdictProduceTheSameFingerprint()
    {
        string a = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded)));
        string b = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded)));
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentIdentityProducesADifferentFingerprint()
    {
        string a = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded), "A"));
        string b = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded), "B"));
        Assert.NotEqual(a, b, StringComparer.Ordinal);
    }

    [Fact]
    public void DifferentVerdictKindForTheSameIdentityProducesADifferentFingerprint()
    {
        string equivalent = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded)));
        string added = ResultFingerprint.Compute(Fixtures.Result(new Added()));
        Assert.NotEqual(equivalent, added, StringComparer.Ordinal);
    }

    [Fact]
    public void DifferentCounterexamplesForTheSameIdentityProduceDifferentFingerprints()
    {
        string first = ResultFingerprint.Compute(Fixtures.Result(new Divergent(Fixtures.Counterexample(1, 2))));
        string second = ResultFingerprint.Compute(Fixtures.Result(new Divergent(Fixtures.Counterexample(1, 3))));
        Assert.NotEqual(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public void DifferentUnknownReasonsForTheSameIdentityProduceDifferentFingerprints()
    {
        string timeout = ResultFingerprint.Compute(Fixtures.Result(new Unknown(UnknownReason.Timeout, "d")));
        string opaque = ResultFingerprint.Compute(Fixtures.Result(new Unknown(UnknownReason.Opaque, "d")));
        Assert.NotEqual(timeout, opaque, StringComparer.Ordinal);
    }

    [Fact]
    public void DifferentUnknownDetailForTheSameIdentityProducesADifferentFingerprint()
    {
        string a = ResultFingerprint.Compute(Fixtures.Result(new Unknown(UnknownReason.Timeout, "a")));
        string b = ResultFingerprint.Compute(Fixtures.Result(new Unknown(UnknownReason.Timeout, "b")));
        Assert.NotEqual(a, b, StringComparer.Ordinal);
    }

    [Fact]
    public void FingerprintIsALowercaseHexSha256Digest()
    {
        string fingerprint = ResultFingerprint.Compute(Fixtures.Result(new Equivalent(ProofMethod.Bounded)));
        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }
}
