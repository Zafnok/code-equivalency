using Equiv.Core;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>Each rung the backend runs is one debug line, and no line is built below debug (ticket M4-014).</summary>
public sealed class BackendProgressTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Theory]
    [InlineData("loops/counter-shape", "bounded:unknown lockstep-induction:unknown k-induction:not-applicable chc:unsat")]
    [InlineData("loops/loop-bound-change", "bounded:sat")]
    [InlineData("loops/irreducible", "bounded:not-applicable lockstep-induction:not-applicable k-induction:not-applicable chc:unsat")]
    public void Each_Rung_Is_One_Detail(string name, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Fixture fixture = Fixture.Load(name);
        RecordingRunLog log = new(isDebug: true);

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, Options with { Log = log });

        Assert.Equal(expected.Split(' '), Parse(log));
        Assert.Equal(verdict.Ladder.Length, log.Events.Count);
    }

    [Fact]
    public void A_Solver_Timeout_Is_Named_Timeout()
    {
        Fixture fixture = Fixture.Load("loops/fusion");
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 1, Log = log });

        Assert.Equal(["chc:timeout", "trace-invariant:unknown"], Parse(log)[^2..]);
    }

    [Fact]
    public void Rung_Five_Is_One_Detail_Whatever_Its_Rounds()
    {
        Fixture fixture = Fixture.Load("loops/fusion");
        RecordingRunLog log = new(isDebug: true);
        Z3Backend backend = new(static () => new Context(), static _ => new FakeInvariantProposer("(assert false)", null));

        backend.Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 1, InvariantModel = "fake-model", Log = log });

        Assert.Single(Parse(log), static l => l.StartsWith("trace-invariant:", StringComparison.Ordinal));
        Assert.Single(Parse(log), static l => l.StartsWith("llm-invariant:", StringComparison.Ordinal));
    }

    [Fact]
    public void No_Detail_Unless_Debug()
    {
        Fixture fixture = Fixture.Load("loops/counter-shape");
        DetailForbiddenLog log = new();

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, Options with { Log = log });

        Assert.IsType<Equivalent>(verdict);
    }

    private static string[] Parse(RecordingRunLog log) =>
        [.. log.Events.Select(static e =>
        {
            string[] parts = e.Split(' ');
            Assert.Equal(4, parts.Length);
            Assert.Equal("detail", parts[0]);
            Assert.StartsWith("took=", parts[2], StringComparison.Ordinal);
            Assert.EndsWith("s", parts[2], StringComparison.Ordinal);
            return $"{parts[1]["rung=".Length..]}:{parts[3]["result=".Length..]}";
        })];

    private sealed class DetailForbiddenLog : IRunLog
    {
        public bool IsDebug => false;

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null) => throw new InvalidOperationException();

        public void Item(string identity, long weight) => throw new InvalidOperationException();

        public void ItemDone(string outcome) => throw new InvalidOperationException();

        public void Detail(string text) => throw new InvalidOperationException("Detail called while IsDebug is false");

        public void PhaseDone() => throw new InvalidOperationException();
    }
}
