using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// ADR 0037's failure-refinement queries (ticket P1-013): whether the modern side can throw where the legacy side returns
/// (<c>newFailures</c>) and the reverse (<c>removedFailures</c>), on every input rung 1 models, and on every resolution of an
/// unshared opaque node or the bound.
/// </summary>
public sealed class FailureRefinementTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>The legacy side returns 0 for a zero divisor; the modern side dropped that guard and divides.</summary>
    [Fact]
    public void NewFailure_Found_WhenGuardRemoved()
    {
        FailureRefinement refinement = Query(
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %isZero: bool = eq %a, %zero
              br %isZero, B1, B2
            B1:
              ret %zero
            B2:
              %ten: bv32 = const bv32 10
              %q: bv32 = sdiv %ten, %a
              ret %q
            """,
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %isZero: bool = eq %a, %zero
              br %isZero, B1, B2
            B1:
              throw "System.DivideByZeroException"
            B2:
              %ten: bv32 = const bv32 10
              %q: bv32 = sdiv %ten, %a
              ret %q
            """);

        Assert.Equal(RefinementOutcome.Found, refinement.NewFailures.Outcome);
        Counterexample model = Assert.IsType<Counterexample>(refinement.NewFailures.Model);
        Assert.Equal(new IrBitVecValue(32, 0), model.Inputs.Arguments[0]);
        Assert.IsType<IrReturned>(model.Old.Outcome);
        Assert.Equal(new IrThrew("System.DivideByZeroException"), model.New.Outcome);
        Assert.Equal(RefinementResult.NoneProved, refinement.RemovedFailures);
    }

    /// <summary>
    /// The pair is Unknown only because a shared fragment is read with other arguments, so only the returned values differ;
    /// neither side throws on any input, so neither query finds a failure. The verdict is unchanged.
    /// </summary>
    [Fact]
    public void NoNewFailure_Proved_WhenOnlyValuesDiffer()
    {
        Verdict verdict = new Z3Backend().Verify(
            IrText.Parse("""
                proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
                B0:
                  %r: bv32 = opaque "DelegateCreation" at "Old.cs" 3:9-3:30 fragment "f1" reads(%a)
                  ret %r
                """),
            IrText.Parse("""
                proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
                B0:
                  %r: bv32 = opaque "DelegateCreation" at "New.cs" 3:9-3:30 fragment "f1" reads(%b)
                  ret %r
                """),
            Options);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal(new FailureRefinement(RefinementResult.NoneProved, RefinementResult.NoneProved), unknown.FailureRefinement);
    }

    /// <summary>
    /// A side that branches on the second answer of a shared fragment throws only through that abstraction, so a model of the
    /// failure replays tainted and the answer is Unknown, never Found. Either side may be the tainted one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaintedFailureModel_IsUnknown(bool legacyBranches)
    {
        string branches = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %first: bv32 = call "opaque:f"(%a)
              %r: bv32 = call "opaque:f"(%a) threw %t: bool
              br %t, B1, B2
            B1:
              throw "System.Exception"
            B2:
              ret %r
            """;
        string returns = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = call "opaque:f"(%a)
              ret %r
            """;

        Unknown unknown = Assert.IsType<Unknown>(legacyBranches
            ? new Z3Backend().Verify(IrText.Parse(branches), IrText.Parse(returns), Options)
            : new Z3Backend().Verify(IrText.Parse(returns), IrText.Parse(branches), Options));

        FailureRefinement refinement = Assert.IsType<FailureRefinement>(unknown.FailureRefinement);
        (RefinementResult tainted, RefinementResult impossible) = legacyBranches
            ? (refinement.RemovedFailures, refinement.NewFailures)
            : (refinement.NewFailures, refinement.RemovedFailures);
        Assert.Equal(RefinementResult.Unknown, tainted);
        Assert.Equal(RefinementResult.NoneProved, impossible);
    }

    /// <summary>
    /// The modern side reaches an unshared opaque node on every input, whose outcome is unknown (ADR 0014): it may throw, so
    /// no new failure is not proved. The legacy side never throws, so no removed failure is.
    /// </summary>
    [Fact]
    public void ModernReachesOpaque_IsNotNoneProved()
    {
        Verdict verdict = new Z3Backend().Verify(
            IrText.Parse("""
                proc "T::M(int)" (%a: bv32) -> bv32 entry B0
                B0:
                  ret %a
                """),
            IrText.Parse("""
                proc "T::M(int)" (%a: bv32) -> bv32 entry B0
                B0:
                  %s: sort "string" = opaque "InterpolatedString" at "New.cs" 5:9-5:30
                  ret %a
                """),
            Options);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal(new FailureRefinement(RefinementResult.Unknown, RefinementResult.NoneProved), unknown.FailureRefinement);
    }

    /// <summary>A timeout pair is not queried (ADR 0037): the verdict carries no refinement.</summary>
    [Fact]
    public void TimeoutPair_IsNotQueried()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");

        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 50 }));

        Assert.Equal(UnknownReason.Timeout, unknown.Reason);
        Assert.Null(unknown.FailureRefinement);
    }

    /// <summary>
    /// A looping pair's inputs past the bound are not modelled, so they count as an opaque node does: the modern loop may
    /// run past it and then throw. Within the bound neither side throws.
    /// </summary>
    [Fact]
    public void PastTheBound_IsUnknown()
    {
        FailureRefinement refinement = Query(
            """
            proc "T::M(int)" (%n: bv32) -> bv32 entry B0
            B0:
              ret %n
            """,
            """
            proc "T::M(int)" (%n: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %limit: bv32 = const bv32 5
              goto B1
            B1:
              %i: bv32 = phi [B0: %zero, B2: %next]
              %more: bool = slt %i, %n
              br %more, B2, B3
            B2:
              %next: bv32 = add %i, %one
              goto B1
            B3:
              %over: bool = sgt %i, %limit
              br %over, B4, B5
            B4:
              throw "System.OverflowException"
            B5:
              ret %n
            """);

        Assert.Equal(RefinementResult.Unknown, refinement.NewFailures);
        Assert.Equal(RefinementResult.NoneProved, refinement.RemovedFailures);
    }

    /// <summary>
    /// A self-call is inlined only <c>k</c> deep, so a recursive legacy side's deeper inputs are past the bound: whether it
    /// throws there is unknown. The modern side never throws, so it has no new failure.
    /// </summary>
    [Fact]
    public void PastTheRecursionBound_IsUnknown()
    {
        FailureRefinement refinement = Query(
            """
            proc "T::F(int)" (%n: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %done: bool = sle %n, %zero
              br %done, B1, B2
            B1:
              ret %zero
            B2:
              %one: bv32 = const bv32 1
              %m: bv32 = sub %n, %one
              %r: bv32 = call "T::F(int)"(%m) threw %t: bool
              br %t, B3, B4
            B3:
              throw "System.Exception"
            B4:
              ret %r
            """,
            """
            proc "T::F(int)" (%n: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              ret %zero
            """);

        Assert.Equal(RefinementResult.NoneProved, refinement.NewFailures);
        Assert.Equal(RefinementResult.Unknown, refinement.RemovedFailures);
    }

    /// <summary>A query the solver gives up on is Unknown.</summary>
    [Fact]
    public void SolverGivesUp_IsUnknown()
    {
        FailureRefinement refinement = new FailureRefinementQuery(static () => new Context(), Options with { TimeoutMs = 1 }).Run(
            IrText.Parse("""
                proc "T::M(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0
                B0:
                  ret %a
                """),
            IrText.Parse("""
                proc "T::M(ulong,ulong)" (%a: bv64, %b: bv64) -> bv64 entry B0
                B0:
                  %q: bv64 = udiv %a, %b
                  %m: bv64 = mul %q, %b
                  %r: bv64 = urem %a, %b
                  %s: bv64 = add %m, %r
                  %same: bool = eq %s, %a
                  br %same, B1, B2
                B1:
                  ret %a
                B2:
                  throw "System.Exception"
                """),
            encodable: true);

        Assert.Equal(RefinementResult.Unknown, refinement.NewFailures);
    }

    /// <summary>A pair rung 1 could not encode has no product, so both answers are Unknown, and the time is still measured.</summary>
    [Fact]
    public void UnencodablePair_IsUnknown()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M()" () entry B0
            B0:
              ret
            """);

        FailureRefinement refinement = new FailureRefinementQuery(static () => new Context(), Options).Run(procedure, procedure, encodable: false);

        Assert.Equal(new FailureRefinement(RefinementResult.Unknown, RefinementResult.Unknown), refinement);
        Assert.True(refinement.Elapsed >= TimeSpan.Zero);
    }

    private static FailureRefinement Query(string old, string @new) =>
        new FailureRefinementQuery(static () => new Context(), Options).Run(IrText.Parse(old), IrText.Parse(@new), encodable: true);
}
