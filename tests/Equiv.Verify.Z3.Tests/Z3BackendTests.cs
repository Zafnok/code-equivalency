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

    [Fact]
    public void ABackEdgeOnEitherSideIsUnknownLoopBeforeAnyZ3Call()
    {
        List<CountingContext> contexts = [];
        Z3Backend backend = new(() => Track(contexts));

        Verdict oldLoops = backend.Verify(IrText.Parse(Loop), IrText.Parse(Straight), Options);
        Verdict newLoops = backend.Verify(IrText.Parse(Straight), IrText.Parse(Loop), Options);

        Assert.Equal(new Unknown(UnknownReason.Loop, "T::Spin(int) has a loop; loops need the M3-002 ladder."), oldLoops);
        Assert.Equal(UnknownReason.Loop, Assert.IsType<Unknown>(newLoops).Reason);
        Assert.Empty(contexts);
    }

    [Fact]
    public void ABlockReachedTwiceWithoutACycleIsNotALoop()
    {
        Verdict verdict = new Z3Backend().Verify(IrText.Parse(Diamond), IrText.Parse(Diamond), Options);

        Assert.IsType<Equivalent>(verdict);
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

        _ = new Z3Backend(() => Track(contexts)).Verify(fixture.Old, fixture.New, Options with { TimeoutMs = 50 });

        CountingContext context = Assert.Single(contexts);
        Assert.Equal(1, context.Disposals);
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
        Assert.StartsWith("solver returned unknown (", unknown.Detail, StringComparison.Ordinal);
        Assert.EndsWith(") with a 50 ms timeout", unknown.Detail, StringComparison.Ordinal);
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
