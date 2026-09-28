using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>The debug log names each rung the backend runs, how long it took and what it found (ticket M4-014).</summary>
public sealed partial class BackendProgressTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void Each_Rung_Is_One_Detail()
    {
        Fixture fusion = Fixture.Load("loops/fusion");
        RecordingRunLog log = new(isDebug: true);
        LoopLadder ladder = new(static () => new Context(), Options with { TimeoutMs = 1, InvariantModel = "fake-model", Log = log }, new FakeInvariantProposer()) { InvariantTimeoutMs = 10_000 };

        Verdict verdict = ladder.Verify(fusion.Old, fusion.New);

        string[] details = [.. log.Events.Select(static e => e["detail ".Length..])];
        Assert.All(details, static d => Assert.Matches(RungLine, d));
        Assert.Equal(verdict.Ladder.Select(static s => s.Rung).Distinct().Count(), details.Length);
        Assert.StartsWith("rung=bounded ", details[0], StringComparison.Ordinal);
        Assert.StartsWith("rung=spacer ", details[^2], StringComparison.Ordinal);
        Assert.EndsWith(" result=timeout", details[^2], StringComparison.Ordinal);
        Assert.StartsWith("rung=llm-invariant ", details[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("loop-bound-change", "rung=bounded ", " result=sat")]
    [InlineData("warm-up", "rung=k-induction ", " result=unsat")]
    public void A_Rung_Reports_Its_Result(string name, string prefix, string suffix)
    {
        Fixture fixture = Fixture.Load("loops/" + name);
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(fixture.Old, fixture.New, Options with { Log = log });

        Assert.Contains(log.Events, e => e.StartsWith("detail " + prefix, StringComparison.Ordinal) && e.EndsWith(suffix, StringComparison.Ordinal));
    }

    [Fact]
    public void No_Detail_Unless_Debug()
    {
        Fixture fusion = Fixture.Load("loops/fusion");
        ThrowingLog log = new();

        new Z3Backend().Verify(fusion.Old, fusion.New, Options with { Log = log });

        Assert.True(log.Asked);
    }

    [GeneratedRegex(@"^rung=[a-z-]+ took=\d+\.\d\d result=(sat|unsat|unknown|timeout)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RungLine { get; }

    private sealed class ThrowingLog : IRunLog
    {
        public bool Asked { get; private set; }

        public bool IsDebug
        {
            get
            {
                Asked = true;
                return false;
            }
        }

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) => throw new InvalidOperationException();

        public void Item(string identity, long weight) => throw new InvalidOperationException();

        public void ItemDone(string outcome) => throw new InvalidOperationException();

        public void Detail(string text) => throw new InvalidOperationException(text);

        public void PhaseDone() => throw new InvalidOperationException();
    }
}
