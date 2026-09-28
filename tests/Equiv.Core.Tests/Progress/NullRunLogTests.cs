using Equiv.Core.Progress;

using Xunit;

namespace Equiv.Core.Tests.Progress;

/// <summary><see cref="NullRunLog"/> accepts every call and is never at debug, and it is what <see cref="VerificationOptions.Log"/> defaults to.</summary>
public sealed class NullRunLogTests
{
    [Fact]
    public void AcceptsEveryCallAndIsNotDebug()
    {
        NullRunLog log = NullRunLog.Instance;

        log.Phase("verify", 1, 1, new PhaseBound(1, 5000, 1));
        log.Item("T::M()", 1);
        log.Detail("text");
        log.ItemDone("equivalent");
        log.PhaseDone();

        Assert.False(log.IsDebug);
    }

    [Fact]
    public void IsTheDefaultLogAndNotPartOfEquality()
    {
        VerificationOptions options = new(3, 5000, []);

        Assert.Same(NullRunLog.Instance, options.Log);
        Assert.Equal(options, options with { Log = new Throwing() });
    }

    private sealed class Throwing : IRunLog
    {
        public bool IsDebug => true;

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) => throw new InvalidOperationException();

        public void Item(string identity, long weight) => throw new InvalidOperationException();

        public void ItemDone(string outcome) => throw new InvalidOperationException();

        public void Detail(string text) => throw new InvalidOperationException();

        public void PhaseDone() => throw new InvalidOperationException();
    }
}
