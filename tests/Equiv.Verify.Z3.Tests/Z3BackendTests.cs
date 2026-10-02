using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>Verdict plumbing of <see cref="Z3Backend"/> (ticket M3-001 criteria 2 and 6, and the timeout test).</summary>
public sealed class Z3BackendTests
{
    private const string Loop = """
        proc "T::Spin(int)" (%a: bv32) entry B0
        B0:
          goto B1
        B1:
          %c: bool = const bool true
          br %c, B1, B2
        B2:
          ret
        """;

    private const string Straight = """
        proc "T::Spin(int)" (%a: bv32) entry B0
        B0:
          ret
        """;

    private const string Diamond = """
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = slt %a, %z
          br %c, B1, B2
        B1:
          %n: bv32 = neg %a
          goto B3
        B2:
          goto B3
        B3:
          %r: bv32 = phi [B1: %n, B2: %a]
          ret %r
        """;

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>
    /// A loop that never exits against a body without one: rungs 2 and 3 do not align them, and rung 4 proves the pair,
    /// since no pair of runs both terminates (partial equivalence, VERIFICATION-MODEL.md section 5.1). Every context rung 4
    /// creates, one per mode it tries, is disposed.
    /// </summary>
    [Fact]
    public void ALoopOnOneSideOnlyClimbsTheLadderToRungFour()
    {
        List<CountingContext> contexts = [];
        Z3Backend backend = new(() => Track(contexts));

        Verdict oldLoops = backend.Verify(IrText.Parse(Loop), IrText.Parse(Straight), Options);
        Verdict newLoops = backend.Verify(IrText.Parse(Straight), IrText.Parse(Loop), Options);

        Assert.Equal(new Equivalent(ProofMethod.Chc), Assert.IsType<Equivalent>(oldLoops) with { Ladder = [], Invariant = null });
        Assert.Equal(
            [
                (ProofMethod.Bounded, RungOutcome.Inconclusive),
                (ProofMethod.LockstepInduction, RungOutcome.NotApplicable),
                (ProofMethod.KInduction, RungOutcome.NotApplicable),
                (ProofMethod.Chc, RungOutcome.Proved),
            ],
            oldLoops.Ladder.Select(static s => (s.Rung, s.Outcome)));
        Assert.Equal("the loops do not align: the old side has 1 loops and the new side 0", oldLoops.Ladder[1].Detail);
        Assert.IsType<Equivalent>(newLoops);
        Assert.All(contexts, static c => Assert.Equal(1, c.Disposals));
    }

    [Fact]
    public void ABlockReachedTwiceWithoutACycleIsNotALoop()
    {
        Verdict verdict = new Z3Backend().Verify(IrText.Parse(Diamond), IrText.Parse(Diamond), Options);

        Assert.Equal(new Equivalent(ProofMethod.Bounded) { Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "no loop or self-call; every input checked")] }, verdict);
    }

    [Theory]
    [InlineData("equivalent-refactor")]
    [InlineData("return-value")]
    [InlineData("opaque-void-effect")]
    [InlineData("hard-multiplication")]
    public void TheContextIsDisposedOnEveryPath(string name)
    {
        Fixture fixture = Fixture.Load(name);
        List<CountingContext> contexts = [];

        Verdict verdict = new Z3Backend(() => Track(contexts)).Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 50 });

        // An Unknown other than a timeout also runs ADR 0037's failure-refinement queries in a context of their own (P1-013).
        Assert.Equal(verdict is Unknown { Reason: not UnknownReason.Timeout } ? 2 : 1, contexts.Count);
        Assert.All(contexts, static c => Assert.Equal(1, c.Disposals));
    }

    [Fact]
    public void TheContextIsDisposedWhenEncodingThrows()
    {
        // Ill-typed IR (IrText.Parse would reject it): a Bool operand of a bitvector add.
        IrVar c = new("c", new IrBool());
        IrProcedure illTyped = new(
            new ProcedureIdentity("T::M(bool)"),
            [new IrParameter(c, IrParameterKind.In)],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [new IrBinary(new IrVar("r", new IrBitVec(32)), IrBinaryOp.Add, c, c)], new IrReturn(Value: null, []))],
            new IrBlockId(0));
        List<CountingContext> contexts = [];

        Assert.ThrowsAny<Exception>(() => new Z3Backend(() => Track(contexts)).Verify(illTyped, illTyped, Options));

        Assert.Equal(1, Assert.Single(contexts).Disposals);
    }

    [Fact]
    public void AHardMultiplicationWithA50MillisecondTimeoutIsUnknownTimeout()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 50 });

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Timeout, unknown.Reason);
        Assert.Equal("solver returned unknown (timeout): wall-clock limit 50 ms hit", unknown.Detail);
    }

    /// <summary>
    /// Ticket P2-050 criterion 2: the solver <see cref="Z3Backend.Query"/> returns, which every query of the product goes
    /// through, stops when it has spent <see cref="VerificationOptions.ResourceLimit"/>, long before the timeout.
    /// </summary>
    [Theory]
    [InlineData(1_000)]
    [InlineData(25_000)]
    public void EveryQuerySetsTheResourceLimit(int resourceLimit)
    {
        Fixture fixture = Fixture.Load("hard-multiplication");
        VerificationOptions options = Options with { TimeoutMs = 600_000, ResourceLimit = resourceLimit };
        using Context context = new();
        ProductEncoder.ProductEncoding encoding = ProductEncoder.Encode(context, fixture.Old, fixture.New, options.CallIdentityMap);

        using Solver solver = Z3Backend.Query(context, encoding, options, encoding.Differs);

        Assert.Equal(Status.UNKNOWN, solver.Check());
        Assert.Equal("canceled", solver.ReasonUnknown);
        Assert.Equal((uint)resourceLimit, solver.Statistics.Entries.Single(static e => string.Equals(e.Key, "rlimit count", StringComparison.Ordinal)).UIntValue);
    }

    /// <summary>
    /// Ticket P2-050 criteria 4 and 5: a query that exhausts the resource limit is Unknown with reason <c>timeout</c>, as a
    /// wall-clock timeout is, and the detail names the limit. The resource limit binds before the wall-clock backstop, and
    /// the verdict is the same on every run.
    /// </summary>
    [Fact]
    public void ResourceExhaustion_IsTimeoutUnknown()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");
        VerificationOptions options = Options with { TimeoutMs = 600_000, ResourceLimit = 1_000 };

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, options);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Timeout, unknown.Reason);
        Assert.Equal("solver returned unknown (canceled): resource limit 1000 hit", unknown.Detail);
        Assert.Equal(UnknownScope.Method, unknown.Scope);
        Assert.Null(unknown.FailureRefinement);
        Assert.Equal(verdict, new Z3Backend().Verify(fixture.Old, fixture.New, options));
    }

    /// <summary>
    /// Ticket P2-050 criterion 4: which limit a reason for giving up names. A solver's timer leaves <c>timeout</c> and a
    /// fixedpoint's <c>canceled</c>; an exhausted <c>rlimit</c> leaves <c>canceled</c> on a tactic solver and Z3's
    /// resource message elsewhere; any other reason names no limit.
    /// </summary>
    [Theory]
    [InlineData("timeout", "timeout", ": wall-clock limit 10000 ms hit")]
    [InlineData("canceled", "canceled", ": wall-clock limit 10000 ms hit")]
    [InlineData("canceled", "timeout", ": resource limit 77 hit")]
    [InlineData("max. resource limit exceeded", "timeout", ": resource limit 77 hit")]
    [InlineData("max. resource limit exceeded", "canceled", ": resource limit 77 hit")]
    [InlineData("(incomplete quantifiers)", "timeout", "")]
    [InlineData("timeout", "canceled", "")]
    public void TheLimitHitIsReadFromZ3sReason(string reason, string timedOut, string expected) =>
        Assert.Equal(expected, Z3Backend.LimitHit(reason, timedOut, resourceLimit: 77, Options.TimeoutMs));

    [Fact]
    public void TimeoutIsMethodScoped()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");

        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 50 }));

        Assert.Equal(UnknownScope.Method, unknown.Scope);
    }

    [Fact]
    public void AnOpaqueBehindAHardConditionTimesOutInTheSecondQuery()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::Rebuild(ulong, ulong)" (%a: bv64, %b: bv64) entry B0
            B0:
              %q: bv64 = udiv %a, %b
              %m: bv64 = mul %q, %b
              %r: bv64 = urem %a, %b
              %s: bv64 = add %m, %r
              %broken: bool = ne %s, %a
              br %broken, B1, B2
            B1:
              opaque "Unreachable in practice" at "T.cs" 9:13-9:30
              ret
            B2:
              ret
            """);

        Verdict verdict = new Z3Backend().Verify(procedure, procedure, Options with { TimeoutMs = 50 });

        Assert.Equal(UnknownReason.Timeout, Assert.IsType<Unknown>(verdict).Reason);
    }

    [Fact]
    public void ABoolInputIsDecodedIntoTheCounterexample()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(bool)" (%c: bool) -> bool entry B0
            B0:
              ret %c
            ---
            proc "T::M(bool)" (%c: bool) -> bool entry B0
            B0:
              %n: bool = boolnot %c
              ret %n
            """);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));

        IrBoolValue input = Assert.IsType<IrBoolValue>(Assert.Single(divergent.Counterexample.Inputs.Arguments));
        Assert.Equal(new IrReturned(input), divergent.Counterexample.Old.Outcome);
    }

    [Fact]
    public void ArgumentsAreRequired()
    {
        IrProcedure procedure = IrText.Parse(Straight);
        Z3Backend backend = new();

        Assert.Throws<ArgumentNullException>("oldBody", () => backend.Verify(null!, procedure, Options));
        Assert.Throws<ArgumentNullException>("newBody", () => backend.Verify(procedure, null!, Options));
        Assert.Throws<ArgumentNullException>("options", () => backend.Verify(procedure, procedure, null!));
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(32, 0)]
    [InlineData(0, 32)]
    public void AReturnTypeChangeDivergesWheneverBothReturn(int oldWidth, int newWidth)
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Returning(oldWidth) + "\n---\n" + Returning(newWidth));

        Verdict verdict = new Z3Backend().Verify(old, @new, Options);

        Divergent divergent = Assert.IsType<Divergent>(verdict);
        Assert.IsType<IrReturned>(divergent.Counterexample.Old.Outcome);
    }

    [Fact]
    public void AReturnTypeChangeAgreesWhenNeitherSideReturns()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M()" () -> bv32 entry B0
            B0:
              throw "System.Exception"
            ---
            proc "T::M()" () -> bv64 entry B0
            B0:
              throw "System.Exception"
            """);

        Assert.IsType<Equivalent>(new Z3Backend().Verify(old, @new, Options));
    }

    [Fact]
    public void TheCallIdentityMapUnifiesALegacyCalleeWithItsModernName()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = call "Legacy.Svc::Get(int)"(%a)
              ret %r
            ---
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = call "Modern.Svc::Get(int)"(%a)
              ret %r
            """);
        ImmutableDictionary<string, string> map = ImmutableDictionary<string, string>.Empty.Add("Legacy.Svc::Get(int)", "Modern.Svc::Get(int)");

        Verdict unmapped = new Z3Backend().Verify(old, @new, Options);
        Verdict mapped = new Z3Backend().Verify(old, @new, Options with { CallIdentityMap = map });
        Verdict reversed = new Z3Backend().Verify(@new, old, Options with { CallIdentityMap = map });

        Divergent divergent = Assert.IsType<Divergent>(unmapped);
        Assert.Equal("Legacy.Svc::Get(int)", Assert.Single(divergent.Counterexample.Old.Trace).Callee.Value);
        Assert.IsType<Equivalent>(mapped);
        Assert.IsType<Divergent>(reversed);
    }

    [Fact]
    public void AMapArgumentIsPartOfTheTraceAndTheCallFunction()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) -> bv32 entry B0
            B0:
              %r: bv32 = call "Svc::Sum(map)"(%field.C.x)
              ret %r outs(%field.C.x = %field.C.x)
            ---
            proc "T::M(int)" (%a: bv32, ref %field.C.x: map<bv32, bv32>) -> bv32 entry B0
            B0:
              %m: map<bv32, bv32> = mapwrite %field.C.x, %a, %a
              %r: bv32 = call "Svc::Sum(map)"(%m)
              ret %r outs(%field.C.x = %field.C.x)
            """);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));

        Assert.NotEqual(divergent.Counterexample.Old.Trace, divergent.Counterexample.New.Trace);
        Assert.IsType<Equivalent>(new Z3Backend().Verify(old, old, Options));
    }

    [Fact]
    public void AParameterThatIsInOnOneSideAndRefOnTheOtherSharesItsInput()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              ret %a
            ---
            proc "T::M(int)" (ref %a: bv32) -> bv32 entry B0
            B0:
              ret %a outs(%a = %a)
            """);

        Assert.IsType<Equivalent>(new Z3Backend().Verify(old, @new, Options));
    }

    [Fact]
    public void ALocalNamedLikeTheOtherSidesParameterIsNotThatInput()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %b: bv32 = const bv32 1
              %r: bv32 = add %a, %b
              ret %r
            ---
            proc "T::M(int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = add %a, %b
              ret %r
            """);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));

        Assert.Equal(2, divergent.Counterexample.Inputs.Arguments.Length);
    }

    /// <summary>
    /// Definitions are substituted into the query in assertion order; an equality whose left side is not a
    /// constant, and any other assertion, is not a definition (ticket M3-027).
    /// </summary>
    [Fact]
    public void InliningSubstitutesDefinitionsInOrderAndSkipsEverythingElse()
    {
        using Context context = new();
        BitVecExpr a = context.MkBVConst("a", 8);
        BitVecExpr b = context.MkBVConst("b", 8);
        BitVecExpr t = context.MkBVConst("t", 8);
        BitVecExpr u = context.MkBVConst("u", 8);
        BoolExpr r = context.MkBoolConst("r");
        BoolExpr s = context.MkBoolConst("s");
        BoolExpr[] assertions =
        [
            r,
            context.MkEq(t, context.MkBVAdd(a, b)),
            context.MkEq(u, context.MkBVMul(t, t)),
            context.MkEq(context.MkBVAdd(a, b), a),
            context.MkNot(s),
        ];

        BoolExpr inlined = Assert.Single(Z3Backend.Inline(context, assertions, [context.MkAnd(r, s, context.MkEq(u, b))]));

        BitVecExpr sum = context.MkBVAdd(a, b);
        Assert.Equal(context.MkAnd(context.MkTrue(), s, context.MkEq(context.MkBVMul(sum, sum), b)), inlined);
    }

    /// <summary>
    /// Z3 5.1's <c>solve-eqs</c> eliminates the shared input <c>b</c> by inverting <c>t25 = t24 + b</c>, which left
    /// this self-comparison (found by <see cref="SoundnessPropertyTests"/>) timing out; with the definitions inlined
    /// into the query it folds in preprocessing (ticket M3-027).
    /// </summary>
    [Fact]
    public void ASelfComparisonWhoseInputFeedsAnAdditionIsEquivalent()
    {
        IrProcedure p = IrText.Parse("""
            proc "Gen.Type::M" (%a "a": bv32, %b "b": bv32, ref %field.Gen.x "field.Gen.x": map<bv32, bv32>) -> bv32 entry B0
            B0:
              %c.0 "c": bv32 = const bv32 11
              %v0.1 "v0": bv32 = const bv32 0
              %v1.2 "v1": bv32 = const bv32 255
              %v2.3 "v2": bv32 = const bv32 9
              %t4: bv32 = const bv32 699007234
              %t5: bv32 = or %v0.1, %t4
              switch %t5 [] default B1
            B1:
              %t6: bv32 = mapread %field.Gen.x, %b
              %t7: bool = sge %t6, %c.0
              br %t7, B2, B3
            B2:
              goto B4
            B3:
              %t11: bv32 = sdiv %v1.2, %b
              %t12: bv8 = trunc %t11
              %t13: bv32 = zext %t12
              %t14: bv32 = neg %a
              %t15: bv32 = mapread %field.Gen.x, %t14
              goto B4
            B4:
              %c.16 "c": bv32 = phi [B2: %c.0, B3: %t13]
              %v0.17 "v0": bv32 = phi [B2: %v0.1, B3: %t15]
              %t18: bv32 = const bv32 2
              %t19: bv32 = and %v1.2, %t18
              %field.Gen.x.20 "field.Gen.x": map<bv32, bv32> = mapwrite %field.Gen.x, %v0.17, %t19
              goto B7
            B7:
              %t22: bv32 = sub %v0.17, %c.16
              %t23: bv32 = mul %v0.17, %t22
              %t24: bv32 = xor %t23, %a
              %t25: bv32 = add %t24, %b
              %t26: bv32 = xor %t25, %c.16
              %t27: bv32 = add %t26, %v0.17
              ret %t27 outs(%field.Gen.x = %field.Gen.x.20)
            """);

        Assert.IsType<Equivalent>(new Z3Backend().Verify(p, p, new VerificationOptions(3, 2_000, [])));
    }

    /// <summary>
    /// Ticket P2-019: no CLR array has a negative length, so a pair whose only divergence needs one is not Divergent. Each
    /// old side reads <c>length.int__</c> at <c>u</c> and differs from the new side only when that length is negative:
    /// <c>u[int.MinValue]</c>, whose unsigned bounds check passes only for a length above 2^31 (the shape M0-012's gate
    /// found), and <c>u.Length &lt; 0</c>.
    /// </summary>
    [Theory]
    [InlineData("""
        %i: bv32 = const bv32 -2147483648
          %in: bool = ult %i, %l
          br %in, B1, B2
        B1:
          %one: bv32 = const bv32 1
          ret %one
        B2:
          throw "System.IndexOutOfRangeException"
        """, """
        throw "System.IndexOutOfRangeException"
        """)]
    [InlineData("""
        %z: bv32 = const bv32 0
          %n: bool = slt %l, %z
          br %n, B1, B2
        B1:
          %one: bv32 = const bv32 1
          ret %one
        B2:
          ret %z
        """, """
        %r: bv32 = const bv32 0
          ret %r
        """)]
    public void ANegativeArrayLengthIsNeverAModel(string oldTail, string newTail)
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair($"""
            proc "T::M(int[])" (%u "u": sort "int[]", %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
            B0:
              %l: bv32 = mapread %length.int__, %u
              {oldTail}
            ---
            proc "T::M(int[])" (%u "u": sort "int[]", %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
            B0:
              {newTail}
            """);

        Assert.IsType<Equivalent>(new Z3Backend().Verify(old, @new, Options));
    }

    /// <summary>
    /// Ticket P2-019 criterion 1: a Divergent model reads a non-negative length at every reference, including those the
    /// procedure never reads, so a replay that asks for another array's length gets a length a CLR array can have.
    /// </summary>
    [Fact]
    public void ADivergentModelGivesEveryReferenceANonNegativeLength()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int[])" (%u "u": sort "int[]", %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
            B0:
              %l: bv32 = mapread %length.int__, %u
              ret %l
            ---
            proc "T::M(int[])" (%u "u": sort "int[]", %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
            B0:
              %l: bv32 = mapread %length.int__, %u
              %one: bv32 = const bv32 1
              %r: bv32 = add %l, %one
              ret %r
            """);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));

        IrMapValue lengths = Assert.IsType<IrMapValue>(divergent.Counterexample.Inputs.Arguments[1]);
        Assert.All(lengths.Entries.Select(static e => e.Value).Prepend(lengths.Default), static v => Assert.True(Assert.IsType<IrBitVecValue>(v).TwosComplement >= 0, $"negative length {v}"));
    }

    /// <summary>A procedure returning bitvector 1 of <paramref name="width"/> bits, or returning nothing when the width is 0.</summary>
    private static string Returning(int width) => width == 0
        ? "proc \"T::M()\" () entry B0\nB0:\n  ret\n"
        : string.Create(CultureInfo.InvariantCulture, $"proc \"T::M()\" () -> bv{width} entry B0\nB0:\n  %x: bv{width} = const bv{width} 1\n  ret %x\n");

    private static CountingContext Track(List<CountingContext> contexts)
    {
        CountingContext context = new();
        contexts.Add(context);
        return context;
    }

    /// <summary>Counts <see cref="IDisposable.Dispose"/> calls; a <c>using</c> on a <see cref="Context"/> dispatches through the interface.</summary>
    private sealed class CountingContext : Context, IDisposable
    {
        public int Disposals { get; private set; }

        void IDisposable.Dispose()
        {
            Disposals++;
            Dispose();
        }
    }
}
