using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Every fixture under <c>Fixtures/</c> gets the verdict its first comment line names (ticket M3-001
/// criteria 3, 5, 8, 10 and 11; ADR 0021), and every Divergent one replays to a divergence that reaches no opaque.
/// </summary>
public sealed class FixtureTests
{
    public static TheoryData<string> Names =>
    [
        "return-value", "out-param", "throw-vs-return", "exception-type", "call-order", "extra-call", "runtime-changed", "equivalent-refactor",
        "opaque-void-effect", "opaque-other-path",
        "heap-write-dropped", "heap-one-sided", "repeated-call",
        "parameters-swapped", "parameter-renamed", "heap-type-changed",
        "hard-multiplication",
        "array-alias",
        "call-heap-order", "call-heap-order-array", "call-heap-same", "call-reads-heap", "call-heap-one-sided",
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureGetsTheVerdictItsFirstLineNames(string name)
    {
        Fixture fixture = Fixture.Load(name);

        Verdict verdict = Verify(fixture);

        Assert.Equal(fixture.Expected, Describe(verdict));
        if (verdict is Divergent divergent)
        {
            Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
            Assert.IsNotType<IrOpaqueReached>(divergent.Counterexample.Old.Outcome);
            Assert.IsNotType<IrOpaqueReached>(divergent.Counterexample.New.Outcome);
        }
    }

    [Fact]
    public void EveryFixtureFileIsListed()
    {
        IEnumerable<string> files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.ir").Select(Path.GetFileNameWithoutExtension)!;

        Assert.Equal(files.Order(StringComparer.Ordinal), Names.Select(static row => row.Data).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Ticket P1-005 acceptance criterion 6: <c>x = 1; Save(this); x = 0;</c> against <c>x = 2; ...</c> diverges at the call,
    /// whose event carries the heap it reads: same callee, same arguments, a different heap.
    /// </summary>
    [Fact]
    public void CallReadsHeapDivergesInTheHeapAtTheCall()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fixture.Load("call-reads-heap")));

        IrCallRecord old = Assert.Single(divergent.Counterexample.Old.Trace);
        IrCallRecord @new = Assert.Single(divergent.Counterexample.New.Trace);
        Assert.Equal(old.Callee, @new.Callee);
        Assert.Equal(old.Arguments, @new.Arguments);
        Assert.NotEqual(old.Heap, @new.Heap);
    }

    /// <summary>Ticket P1-005: a side that does not name a map the other side's call writes reports the version the replay threaded through its own call.</summary>
    [Fact]
    public void AOneSidedHeapDivergenceReplaysThroughTheThreadedVersion()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "C::M()" (ref %field.C.f: map<sort "C", bv32>, %this: sort "C") entry B0
            B0:
              call "C::Foo()"(%this) heap("field.C.f" %field.C.f -> %f1: map<sort "C", bv32>)
              %one: bv32 = const bv32 1
              %f2: map<sort "C", bv32> = mapwrite %f1, %this, %one
              ret outs(%field.C.f = %f2)
            ---
            proc "C::M()" (%this: sort "C") entry B0
            B0:
              call "C::Foo()"(%this)
              ret
            """);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, new VerificationOptions(3, 10_000, [])));

        Assert.Equal(divergent.Counterexample.Old.Trace, divergent.Counterexample.New.Trace);
        Assert.Equal(divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome);
    }

    [Fact]
    public void RuntimeChangedCallIsDivergentWithRuleEq006()
    {
        Fixture fixture = Fixture.Load("runtime-changed");

        Divergent divergent = Assert.IsType<Divergent>(Verify(fixture));

        IrReturned oldResult = Assert.IsType<IrReturned>(divergent.Counterexample.Old.Outcome);
        IrReturned newResult = Assert.IsType<IrReturned>(divergent.Counterexample.New.Outcome);
        Assert.NotEqual(oldResult, newResult);
        string ruleId = SarifReportWriter.Write([new VerificationResult(fixture.Old.Identity, divergent)]).Runs[0].Results[0].RuleId;
        Assert.Equal("EQ006", ruleId);
    }

    [Fact]
    public void RepeatedCallReplaysWithDifferentResultsPerPosition()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fixture.Load("repeated-call")));

        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), divergent.Counterexample.Old.Outcome);
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), divergent.Counterexample.New.Outcome);
    }

    [Fact]
    public void HeapWriteDroppedDivergesOnTheFinalHeapOnly()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fixture.Load("heap-write-dropped")));

        Assert.Equal(divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome);
        Assert.NotEqual(divergent.Counterexample.Old.Outs, divergent.Counterexample.New.Outs);
    }

    /// <summary>
    /// Ticket P1-006 acceptance criterion 3: the aliased-array procedure is Divergent against the variant that returns 2
    /// only on distinct arrays, since one array passed twice makes it return 2 too, and Equivalent against itself.
    /// </summary>
    [Fact]
    public void ArrayAliasDivergesOnDistinctArraysAndIsEquivalentToItself()
    {
        Fixture fixture = Fixture.Load("array-alias");

        Divergent divergent = Assert.IsType<Divergent>(Verify(fixture));

        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, 1)), divergent.Counterexample.Old.Outcome);
        Assert.IsType<Equivalent>(Verify(fixture with { New = fixture.Old }));
    }

    [Fact]
    public void OpaqueDetailNamesTheReachableOpaque()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(Fixture.Load("opaque-void-effect")));

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal("old: CompoundAssignment", unknown.Detail);
        Assert.Equal(new SourceSpan("T.cs", 3, 9, 3, 15), Assert.Single(unknown.Causes).Span);
    }

    /// <summary>A fixture that expects a timeout gets 50 ms; every other one gets ten seconds.</summary>
    private static Verdict Verify(Fixture fixture) =>
        new Z3Backend().Verify(fixture.Old, fixture.New, new VerificationOptions(3, string.Equals(fixture.Expected, "Unknown(Timeout)", StringComparison.Ordinal) ? 50 : 10_000, []));

    private static string Describe(Verdict verdict) => verdict switch
    {
        Unknown unknown => $"Unknown({unknown.Reason})",
        _ => verdict.GetType().Name,
    };
}
