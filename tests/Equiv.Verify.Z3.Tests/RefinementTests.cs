using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Equiv.Verify.Z3.Refinement;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Abstraction refinement (ADR 0053; ticket P1-030): a pair rung 1 leaves Unknown(abstraction) on a candidate that
/// depends only on interpretable pure functions is asked again with those functions given their real meaning.
/// </summary>
public sealed class RefinementTests
{
    private const string Double = "sort \"System.Double\"";

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void Refined_UnsatisfiableIsEquivalent()
    {
        Verdict verdict = Verify(
            $"""
            proc "T::M(double)" (%a: {Double}) -> {Double} entry B0
            B0:
              %two: {Double} = const {Literal(2.0)}
              %s: {Double} = pure "f64.mul"(%a, %two)
              ret %s
            """,
            $"""
            proc "T::M(double)" (%a: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %a)
              ret %s
            """);

        Equivalent equivalent = Assert.IsType<Equivalent>(verdict);
        Assert.Equal(ProofMethod.Bounded, equivalent.Method);
        Assert.Equal(2, verdict.Ladder.Length);
        Assert.Equal(RungOutcome.Inconclusive, verdict.Ladder[0].Outcome);
        Assert.Empty(verdict.Ladder[0].Refined);
        Assert.Equal(RungOutcome.Proved, verdict.Ladder[1].Outcome);
        Assert.Equal(["f64.add", "f64.mul"], verdict.Ladder[1].Refined);
    }

    [Fact]
    public void Refined_ModelReplaysUntainted()
    {
        Verdict verdict = Verify(Binary("f64.add", "%a", "%b"), Binary("f64.sub", "%a", "%b"));

        Divergent divergent = Assert.IsType<Divergent>(verdict);
        Counterexample model = divergent.Counterexample;
        double a = IrFloat.ToDouble((IrSortValue)model.Inputs.Arguments[0]);
        double b = IrFloat.ToDouble((IrSortValue)model.Inputs.Arguments[1]);
        Assert.Equal(new IrReturned(IrFloat.Of(a + b)), model.Old.Outcome);
        Assert.Equal(new IrReturned(IrFloat.Of(a - b)), model.New.Outcome);
        Assert.NotEqual(model.Old.Outcome, model.New.Outcome);
        Assert.Equal(IrTaint.None, model.Old.Taint);
        Assert.Equal(IrTaint.None, model.New.Taint);
        Assert.Equal(["f64.add", "f64.sub"], verdict.Ladder[^1].Refined);
        Assert.Equal(RungOutcome.Refuted, verdict.Ladder[^1].Outcome);
    }

    [Fact]
    public void Refined_AssociativityOfAdditionIsDivergent()
    {
        Verdict verdict = Verify(
            $"""
            proc "T::M(double,double,double)" (%a: {Double}, %b: {Double}, %c: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %b)
              %t: {Double} = pure "f64.add"(%s, %c)
              ret %t
            """,
            $"""
            proc "T::M(double,double,double)" (%a: {Double}, %b: {Double}, %c: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%b, %c)
              %t: {Double} = pure "f64.add"(%a, %s)
              ret %t
            """);

        Divergent divergent = Assert.IsType<Divergent>(verdict);
        double[] inputs = [.. divergent.Counterexample.Inputs.Arguments.Select(static v => IrFloat.ToDouble((IrSortValue)v))];
        Assert.NotEqual(IrFloat.Of((inputs[0] + inputs[1]) + inputs[2]), IrFloat.Of(inputs[0] + (inputs[1] + inputs[2])));
    }

    [Fact]
    public void Refinement_RunsOnlyWhenEveryAbstractionIsInterpretable()
    {
        // The candidate reaches f64.rem too, which has no meaning here, so nothing is interpreted, f64.add included.
        Verdict verdict = Verify(
            $"""
            proc "T::M(double,double)" (%a: {Double}, %b: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %b)
              %r: {Double} = pure "f64.rem"(%s, %b)
              ret %r
            """,
            $"""
            proc "T::M(double,double)" (%a: {Double}, %b: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%b, %a)
              %r: {Double} = pure "f64.rem"(%s, %b)
              ret %r
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal("the divergence depends on f64.add, f64.rem", unknown.Detail);
        Assert.Single(verdict.Ladder);
        Assert.Empty(verdict.Ladder[0].Refined);
    }

    [Theory]
    [InlineData("f64.add", "f64.add", true)]
    [InlineData("f64.add", "f64.rem", false)]
    [InlineData("f64.add", "dec.add", false)]
    [InlineData("f64.add", "opaque:abc", false)]
    [InlineData("f64.add", "x87.f64.add", false)]
    public void Next_NeedsEveryAbstractionInterpretable(string first, string second, bool refined)
    {
        (IrProcedure old, IrProcedure @new) = (IrText.Parse(Binary(first, "%a", "%b")), IrText.Parse(Binary(second, "%a", "%b")));
        ImmutableArray<Abstraction> abstractions = [new(Codebase.Legacy, new CallIdentity(first), Span: null), new(Codebase.Modern, new CallIdentity(second), Span: null)];

        ImmutableSortedSet<string>? next = AbstractionRefinement.Next(old, @new, ImmutableSortedSet.Create<string>(StringComparer.Ordinal), abstractions);

        Assert.Equal(refined, next is not null);
        if (refined)
        {
            Assert.Equal([first], next!);
        }
    }

    [Fact]
    public void Next_AddsToWhatIsInterpretedAndStopsWhenNothingIsNew()
    {
        (IrProcedure old, IrProcedure @new) = (IrText.Parse(Binary("f64.add", "%a", "%b")), IrText.Parse(Binary("f64.sub", "%a", "%b")));
        ImmutableSortedSet<string> interpreted = ImmutableSortedSet.Create(StringComparer.Ordinal, "f64.sub");
        ImmutableArray<Abstraction> add = [new(Codebase.Legacy, new CallIdentity("f64.add"), Span: null)];
        ImmutableArray<Abstraction> sub = [new(Codebase.Modern, new CallIdentity("f64.sub"), Span: null)];

        Assert.Equal(["f64.add", "f64.sub"], AbstractionRefinement.Next(old, @new, interpreted, add)!);
        Assert.Null(AbstractionRefinement.Next(old, @new, interpreted, sub));
        Assert.Null(AbstractionRefinement.Next(old, @new, interpreted, []));
    }

    [Fact]
    public void Refinement_NeverInterpretsARuntimeSensitiveFunction()
    {
        (IrProcedure old, IrProcedure @new) = (IrText.Parse(Binary("f64.add", "%a", "%b", sensitive: true)), IrText.Parse(Binary("f64.add", "%b", "%a")));

        Assert.Empty(AbstractionRefinement.Interpretable(old, @new));
        Assert.Equal(["f64.add"], AbstractionRefinement.Interpretable(@new, @new));
        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(old, @new, Options));
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Single(unknown.Ladder);
    }

    [Fact]
    public void Refinement_NeverInterpretsACheckedConversion()
    {
        const string Checked =
            """
            proc "T::M(double)" (%a: sort "System.Double") -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a) throws(%o: bool "System.OverflowException")
              ret %i
            """;
        const string Unchecked =
            """
            proc "T::M(double)" (%a: sort "System.Double") -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a)
              ret %i
            """;

        Assert.Empty(AbstractionRefinement.Interpretable(IrText.Parse(Checked), IrText.Parse(Unchecked)));
        Assert.Equal(["conv.f64.i32"], AbstractionRefinement.Interpretable(IrText.Parse(Unchecked), IrText.Parse(Unchecked)));
    }

    /// <summary>
    /// Each path of the pair diverges only until one more function has its meaning, and a candidate takes one path. Three
    /// paths are proved in three rounds; a fourth is one round too many, and the pair keeps its first Unknown.
    /// </summary>
    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Refinement_StopsAfterThreeRounds(int paths, bool decided)
    {
        Verdict verdict = Verify(Paths(paths, swapped: false), Paths(paths, swapped: true));

        if (decided)
        {
            Assert.IsType<Equivalent>(verdict);
            Assert.Equal(4, verdict.Ladder.Length);
            Assert.Equal([1, 2, 3], verdict.Ladder.Skip(1).Select(static s => s.Refined.Length));
            Assert.All(verdict.Ladder.Skip(1).Take(2), static s => Assert.Equal(RungOutcome.Inconclusive, s.Outcome));
        }
        else
        {
            Unknown unknown = Assert.IsType<Unknown>(verdict);
            Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
            Assert.Single(verdict.Ladder);
            Assert.Single(unknown.Abstractions.Select(static a => a.Identity).Distinct());
        }
    }

    [Fact]
    public void Refinement_TimeoutKeepsTheAbstractionUnknown()
    {
        // Halving is dividing by two in IEEE arithmetic, which no solver shows within this resource limit; the first,
        // uninterpreted query is satisfiable at once.
        string old =
            $"""
            proc "T::M(double)" (%a: {Double}) -> {Double} entry B0
            B0:
              %half: {Double} = const {Literal(0.5)}
              %s: {Double} = pure "f64.mul"(%a, %half)
              ret %s
            """;
        string @new =
            $"""
            proc "T::M(double)" (%a: {Double}) -> {Double} entry B0
            B0:
              %two: {Double} = const {Literal(2.0)}
              %s: {Double} = pure "f64.div"(%a, %two)
              ret %s
            """;

        Verdict verdict = new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options with { ResourceLimit = 20_000, RefineTimeouts = false });

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal("the divergence depends on f64.mul, f64.div", unknown.Detail);
        Assert.NotNull(unknown.Candidate);
        Assert.Single(verdict.Ladder);
    }

    [Fact]
    public void Refined_OpaqueReachedIsUnknownOpaqueWithTheResidualClaim()
    {
        const string Tail =
            """
              br %c, B1, B2
            B1:
              opaque "Lambda" at "f.cs" 3:1-3:9
              ret %s
            B2:
              ret %s
            """;

        Verdict verdict = Verify(
            $"""
            proc "T::M(double,double,bool)" (%a: {Double}, %b: {Double}, %c: bool) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %b)
            {Tail}
            """,
            $"""
            proc "T::M(double,double,bool)" (%a: {Double}, %b: {Double}, %c: bool) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%b, %a)
            {Tail}
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal(UnknownScope.Line, unknown.Scope);
        Assert.Equal(["f64.add"], verdict.Ladder[^1].Refined);
    }

    [Fact]
    public void Refined_IntPtrEqualityIsSortEquality()
    {
        const string Pointer = "sort \"System.IntPtr\"";
        const string Equality = "op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)";
        const string Inequality = "op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)";
        string same =
            $"""
            proc "T::M(System.IntPtr,System.IntPtr)" (%a: {Pointer}, %b: {Pointer}) -> bool entry B0
            B0:
              %c: bool = pure "{Equality}"(%a, %b) throws(%o: bool "System.Exception")
              br %o, B1, B2
            B1:
              throw "System.Exception"
            B2:
              ret %c
            """;
        string negated =
            $"""
            proc "T::M(System.IntPtr,System.IntPtr)" (%a: {Pointer}, %b: {Pointer}) -> bool entry B0
            B0:
              %d: bool = pure "{Inequality}"(%a, %b) throws(%o: bool "System.Exception")
              br %o, B1, B2
            B1:
              throw "System.Exception"
            B2:
              %c: bool = boolnot %d
              ret %c
            """;

        Assert.IsType<Equivalent>(Verify(same, negated));
        Divergent divergent = Assert.IsType<Divergent>(Verify(same, negated.Replace("%c: bool = boolnot %d", "%c: bool = or %d, %d", StringComparison.Ordinal)));
        Assert.Equal(IrTaint.None, divergent.Counterexample.Old.Taint);
        Assert.IsType<IrReturned>(divergent.Counterexample.Old.Outcome);
    }

    [Fact]
    public void Refined_ConversionOutOfRangeStaysTheSharedFunction()
    {
        // In range, truncating twice is truncating once. Out of range the conversion is still the function both sides
        // share, so the first conversion gives one value on both sides.
        string once =
            $"""
            proc "T::M(double)" (%a: {Double}) -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a)
              ret %i
            """;
        string twice =
            $"""
            proc "T::M(double)" (%a: {Double}) -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a)
              %d: {Double} = pure "conv.i32.f64"(%i)
              %j: bv32 = pure "conv.f64.i32"(%d)
              ret %j
            """;

        Verdict verdict = Verify(once, twice);

        // Whatever the shared function gives out of range is an int, and an int converts to double and back to itself.
        Assert.IsType<Equivalent>(verdict);
        Assert.Equal(["conv.f64.i32", "conv.i32.f64"], verdict.Ladder[^1].Refined);
    }

    [Fact]
    public void Refined_ConversionOutOfRangeIsStillAnAbstraction()
    {
        // -(int)(-a) is (int)a wherever both truncations fit. Where one does not, the shared function decides, the replay
        // taints it, and there is nothing left to interpret: the pair keeps the Unknown it had.
        string direct =
            $"""
            proc "T::M(double)" (%a: {Double}) -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a)
              ret %i
            """;
        string negated =
            $"""
            proc "T::M(double)" (%a: {Double}) -> bv32 entry B0
            B0:
              %n: {Double} = pure "f64.neg"(%a)
              %i: bv32 = pure "conv.f64.i32"(%n)
              %j: bv32 = neg %i
              ret %j
            """;

        Verdict verdict = Verify(direct, negated);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Single(verdict.Ladder);
        Assert.Contains(unknown.Abstractions, static a => string.Equals(a.Identity.Value, "f64.neg", StringComparison.Ordinal));
    }

    [Fact]
    public void NaNComparisons_AreAllFalseButNotEqual()
    {
        foreach (string comparison in (string[])["eq", "lt", "le", "gt", "ge"])
        {
            Divergent divergent = Assert.IsType<Divergent>(Verify(Compare("f64." + comparison, "%a", "%a"), Constant(value: true)));
            if (comparison is "eq" or "le" or "ge")
            {
                Assert.Equal(IrFloat.Of(double.NaN), divergent.Counterexample.Inputs.Arguments[0]);
                Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), divergent.Counterexample.Old.Outcome);
            }
        }

        Divergent unequal = Assert.IsType<Divergent>(Verify(Compare("f64.ne", "%a", "%a"), Constant(value: false)));
        Assert.Equal(IrFloat.Of(double.NaN), unequal.Counterexample.Inputs.Arguments[0]);
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), unequal.Counterexample.Old.Outcome);
        Assert.IsType<Equivalent>(Verify(Compare("f64.lt", "%a", "%a"), Constant(value: false)));
        Assert.IsType<Equivalent>(Verify(Compare("f64.gt", "%a", "%a"), Constant(value: false)));
    }

    [Fact]
    public void Refined_SignedZerosAreEqualAsNumbersAndDifferAsValues()
    {
        // 0.0 == -0.0 holds, and returning one for the other is a divergence.
        string zero = $"proc \"T::M()\" () -> {Double} entry B0\nB0:\n  %z: {Double} = const {Literal(0.0)}\n  ret %z";
        string negative = $"proc \"T::M()\" () -> {Double} entry B0\nB0:\n  %z: {Double} = const {Literal(-0.0)}\n  ret %z";
        string compared =
            $"""
            proc "T::M()" () -> bool entry B0
            B0:
              %z: {Double} = const {Literal(0.0)}
              %n: {Double} = pure "f64.neg"(%z)
              %c: bool = pure "f64.eq"(%z, %n)
              ret %c
            """;
        string yes = "proc \"T::M()\" () -> bool entry B0\nB0:\n  %c: bool = const bool true\n  ret %c";

        Assert.IsType<Divergent>(Verify(zero, negative));
        Assert.IsType<Equivalent>(Verify(compared, yes));
    }

    private static Verdict Verify(string old, string @new) => new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);

    private static string Literal(double value) => $"{Double} {IrFloat.Of(value).Id.ToString(CultureInfo.InvariantCulture)}";

    private static string Binary(string function, string left, string right, bool sensitive = false) =>
        $"""
        proc "T::M(double,double)" (%a: {Double}, %b: {Double}) -> {Double} entry B0
        B0:
          %s: {Double} = pure "{function}"{(sensitive ? "!" : string.Empty)}({left}, {right})
          ret %s
        """;

    private static string Compare(string function, string left, string right) =>
        $"""
        proc "T::M(double)" (%a: {Double}) -> bool entry B0
        B0:
          %c: bool = pure "{function}"({left}, {right})
          ret %c
        """;

    private static string Constant(bool value) =>
        $"""
        proc "T::M(double)" (%a: {Double}) -> bool entry B0
        B0:
          %c: bool = const bool {(value ? "true" : "false")}
          ret %c
        """;

    /// <summary>
    /// A procedure that switches on <c>%k</c> to one of <paramref name="paths"/> blocks, each passing one commutative
    /// function of <c>%a</c> and <c>%b</c> to a sink, with the operands <paramref name="swapped"/> or not.
    /// </summary>
    private static string Paths(int paths, bool swapped)
    {
        (string Function, string Type, string Sink)[] functions =
        [
            ("f64.add", Double, "Sink::Number(double)"), ("f64.eq", "bool", "Sink::Flag(bool)"),
            ("f64.ne", "bool", "Sink::Other(bool)"), ("f32.add", "sort \"System.Single\"", "Sink::Small(float)"),
        ];
        (string left, string right) = swapped ? ("b", "a") : ("a", "b");
        IEnumerable<string> blocks = functions.Take(paths).Select((f, i) =>
        {
            string suffix = f.Function.StartsWith("f32", StringComparison.Ordinal) ? "f" : string.Empty;
            return $"B{(i + 1).ToString(CultureInfo.InvariantCulture)}:\n  %r{i.ToString(CultureInfo.InvariantCulture)}: {f.Type} = pure \"{f.Function}\"(%{left}{suffix}, %{right}{suffix})\n"
                + $"  call \"{f.Sink}\"(%r{i.ToString(CultureInfo.InvariantCulture)})\n  ret";
        });
        string cases = string.Join(", ", Enumerable.Range(1, paths - 1).Select(static i => $"bv32 {i.ToString(CultureInfo.InvariantCulture)} -> B{(i + 1).ToString(CultureInfo.InvariantCulture)}"));
        return $"proc \"T::M(int,double,double,float,float)\" (%k: bv32, %a: {Double}, %b: {Double}, %af: sort \"System.Single\", %bf: sort \"System.Single\") entry B0\n"
            + $"B0:\n  switch %k [{cases}] default B1\n{string.Join('\n', blocks)}";
    }
}
