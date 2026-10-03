using System.Globalization;

using Equiv.Core;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Each rung the backend runs is one debug line (ticket M4-014), each stage of a pair's work is one before its rung's or
/// its step's (ticket P2-076 criterion 1), and no line is built below debug.
/// </summary>
public sealed class BackendProgressTests
{
    private const string RungOne = "share shape unroll encode assert inline ";
    private const string Proved = "encode assert inline check:obligation=unsat dispose ";
    private const string Failed = "encode assert inline check:obligation=sat dispose ";

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
        Assert.Equal(verdict.Ladder.Length, Parse(log).Length);
    }

    /// <summary>
    /// Ticket P2-076 criterion 1: encoding, <c>Inline</c>, each solver query with its result and each step after the ladder,
    /// in the order the pair did them. A stage is its name, a query <c>check:&lt;query&gt;=&lt;result&gt;</c>, and a rung or a
    /// step is prefixed with its kind.
    /// </summary>
    [Theory]
    [InlineData("equivalent-refactor", RungOne + "check:divergence=unsat assert inline check:opaque=unsat dispose rung:bounded=unsat")]
    [InlineData("loops/loop-bound-change", RungOne + "check:divergence=sat replay dispose rung:bounded=sat")]
    [InlineData(
        "opaque-void-effect",
        RungOne + "check:divergence=unsat assert inline check:opaque=sat step:reachable-opaques dispose rung:bounded=unknown "
        + "unroll encode assert inline check:failure-modelled=unsat assert inline check:failure-resolved=unsat "
        + "assert inline check:failure-modelled=unsat assert inline check:failure-resolved=sat dispose step:failure-refinement")]
    [InlineData(
        "loops/warm-up",
        RungOne + "check:divergence=unsat assert inline check:opaque=unsat assert inline check:bound=sat dispose rung:bounded=unknown "
        + "couple " + Proved + Failed + "rung:lockstep-induction=unknown "
        + Proved + Proved + "rung:k-induction=unsat")]
    [InlineData(
        "loops/late-divergence-beyond",
        RungOne + "check:divergence=unsat assert inline check:opaque=unsat assert inline check:bound=sat dispose rung:bounded=unknown "
        + "couple " + Proved + Failed + "rung:lockstep-induction=unknown "
        + Proved + Failed + "rung:k-induction=unknown "
        + "encode-chc check:spacer=sat dispose rung:chc=sat")]
    public void Each_Stage_Is_One_Detail_In_Order(string name, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Fixture fixture = Fixture.Load(name);
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(fixture.Old, fixture.New, Options with { Log = log });

        Assert.Equal(expected, string.Join(' ', Details(log)));
    }

    /// <summary>
    /// The model of rung 1's second query reaches one opaque node; the search for the others is one query each, until one
    /// finds no input, and the step's line holds them all.
    /// </summary>
    [Fact]
    public void The_Search_For_Every_Reachable_Opaque_Is_A_Step()
    {
        (Equiv.Core.Ir.IrProcedure old, Equiv.Core.Ir.IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %negative: bool = slt %a, %zero
              br %negative, B1, B2
            B1:
              opaque "Lambda" at "Old.cs" 5:13-5:30
              ret %zero
            B2:
              %never: bool = const bool false
              br %never, B3, B4
            B3:
              opaque "Dead" at "Old.cs" 9:13-9:30
              ret %a
            B4:
              ret %a
            ---
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %negative: bool = slt %a, %zero
              br %negative, B1, B2
            B1:
              ret %zero
            B2:
              opaque "Await" at "New.cs" 7:9-7:20
              ret %a
            """);
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(old, @new, Options with { Log = log });

        string[] details = Details(log);
        int opaque = Array.IndexOf(details, "check:opaque=sat");
        Assert.Equal(
            ["assert", "inline", "check:reachable-opaque=sat", "assert", "inline", "check:reachable-opaque=unsat", "step:reachable-opaques", "dispose", "rung:bounded=unknown"],
            details[(opaque + 1)..(opaque + 10)]);
    }

    [Fact]
    public void A_Proof_Spacer_Finds_Is_Certified_In_A_Stage_Of_Its_Own()
    {
        Fixture fixture = Fixture.Load("loops/fusion");
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 60_000, Log = log });

        Assert.Equal(
            ["encode-chc", "check:spacer=unsat", "encode-chc", "check:certificate=sat", "check:certificate=unsat", "check:spacer=unsat", "dispose", "rung:chc=unsat"],
            Details(log)[^8..]);
    }

    [Fact]
    public void A_Solver_Timeout_Is_Named_Timeout()
    {
        Fixture fixture = Fixture.Load("loops/fusion");
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 1, Log = log });

        Assert.Equal(["chc:timeout", "trace-invariant:unknown"], Parse(log)[^2..]);
        Assert.Contains("check:spacer=unknown", Details(log), StringComparer.Ordinal);
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
        string[] details = Details(log);
        int traces = Array.IndexOf(details, "rung:trace-invariant=unknown");
        int model = Array.IndexOf(details, "rung:llm-invariant=unknown");
        Assert.Equal(["encode-chc", "propose", "propose", "dispose"], details[(traces + 1)..model]);
        string[] mined = details[(Array.IndexOf(details, "rung:chc=timeout") + 1)..traces];
        Assert.Equal(["encode-chc", "propose"], mined[..2]);
        Assert.StartsWith("check:invariant=", mined[2], StringComparison.Ordinal);
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
        [.. log.Events.Where(static e => e.StartsWith("detail rung=", StringComparison.Ordinal)).Select(static e =>
        {
            string[] parts = e.Split(' ');
            Assert.Equal(4, parts.Length);
            Assert.Equal("detail", parts[0]);
            Assert.StartsWith("took=", parts[2], StringComparison.Ordinal);
            Assert.EndsWith("s", parts[2], StringComparison.Ordinal);
            return $"{parts[1]["rung=".Length..]}:{parts[3]["result=".Length..]}";
        })];

    /// <summary>Every detail in order: a stage as its name, a rung or a step as <c>kind:name</c>, each with <c>=result</c> when it has one.</summary>
    internal static string[] Details(RecordingRunLog log) =>
        [.. log.Events.Select(static e =>
        {
            string[] parts = e.Split(' ');
            Assert.Equal("detail", parts[0]);
            Assert.InRange(parts.Length, 3, 4);
            Assert.StartsWith("took=", parts[2], StringComparison.Ordinal);
            Assert.EndsWith("s", parts[2], StringComparison.Ordinal);
            Assert.InRange(double.Parse(parts[2]["took=".Length..^1], CultureInfo.InvariantCulture), 0, 600);
            string[] name = parts[1].Split('=');
            Assert.Equal(2, name.Length);
            string result = parts.Length == 4 ? "=" + parts[3]["result=".Length..] : string.Empty;
            return (string.Equals(name[0], "stage", StringComparison.Ordinal) ? name[1] : $"{name[0]}:{name[1]}") + result;
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
