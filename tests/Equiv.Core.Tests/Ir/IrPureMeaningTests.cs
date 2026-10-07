using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// The pure functions with a real meaning (ADR 0053; ticket P1-030): how a floating-point number is an IR value
/// (<see cref="IrFloat"/>), which applications are interpretable and what they compute (<see cref="IrPureMeaning"/>), and
/// how <see cref="IrInterpreter"/> computes an interpreted function itself, untainted.
/// </summary>
public sealed class IrPureMeaningTests
{
    private const string Double = "sort \"System.Double\"";

    private static readonly IrBool Bool = new();

    [Fact]
    public void ANumberIsTheElementOfItsBits()
    {
        Assert.Equal(new IrSortValue("System.Double", 0x3FF0_0000_0000_0000), IrFloat.Of(1.0));
        Assert.Equal(new IrSortValue("System.Single", 0x3F80_0000), IrFloat.Of(1f));
        Assert.Equal(new IrSortValue("System.Single", 0xBF80_0000), IrFloat.Of(-1f));
        Assert.Equal(new IrSortValue("System.Double", unchecked((long)0xBFF0_0000_0000_0000)), IrFloat.Of(-1.0));
        Assert.Equal(new IrSortValue("System.Double", 0), IrFloat.Of(0.0));
        Assert.NotEqual(IrFloat.Of(0.0), IrFloat.Of(-0.0));
        Assert.NotEqual(IrFloat.Of(0f), IrFloat.Of(-0f));
        Assert.Equal(1.5, IrFloat.ToDouble(IrFloat.Of(1.5)));
        Assert.Equal(-2.5f, IrFloat.ToSingle(IrFloat.Of(-2.5f)));
        Assert.Equal(IrFloat.Binary64, IrFloat.Of(1.0).Type);
        Assert.Equal(IrFloat.Binary32, IrFloat.Of(1f).Type);
    }

    [Fact]
    public void EveryNaNIsTheOneNaN()
    {
        Assert.Equal(new IrSortValue("System.Double", 0x7FF8_0000_0000_0000), IrFloat.Of(double.NaN));
        Assert.Equal(new IrSortValue("System.Single", 0x7FC0_0000), IrFloat.Of(float.NaN));
        Assert.Equal(IrFloat.Of(double.NaN), IrFloat.Of(-double.NaN));
        Assert.Equal(IrFloat.Of(double.NaN), IrFloat.Of(BitConverter.UInt64BitsToDouble(0x7FF0_0000_0000_0001)));
        Assert.Equal(IrFloat.Of(float.NaN), IrFloat.Of(BitConverter.UInt32BitsToSingle(0xFFC0_1234)));
        Assert.Equal(IrFloat.Of(double.NaN), IrFloat.OfBits(64, ulong.MaxValue));
        Assert.Equal(IrFloat.Of(float.NaN), IrFloat.OfBits(32, uint.MaxValue));
        Assert.True(double.IsNaN(IrFloat.ToDouble(IrFloat.Of(double.NaN))));
        Assert.True(float.IsNaN(IrFloat.ToSingle(IrFloat.Of(float.NaN))));
    }

    [Fact]
    public void BitsBecomeTheElementOfTheirWidth()
    {
        Assert.Equal(IrFloat.Of(2.0), IrFloat.OfBits(64, 0x4000_0000_0000_0000));
        Assert.Equal(IrFloat.Of(2f), IrFloat.OfBits(32, 0x4000_0000));
        Assert.Equal(IrFloat.Of(double.NegativeInfinity), IrFloat.OfBits(64, 0xFFF0_0000_0000_0000));
        Assert.Equal(IrFloat.Of(-0f), IrFloat.OfBits(32, 0x8000_0000));
    }

    [Fact]
    public void ANumberReadsAsTheShortestTextThatGivesItsBitsBack()
    {
        Assert.Equal("f64 0.1", IrFloat.Text(IrFloat.Of(0.1)));
        Assert.Equal("f32 0.1", IrFloat.Text(IrFloat.Of(0.1f)));
        Assert.Equal("f64 0.10000000149011612", IrFloat.Text(IrFloat.Of((double)0.1f)));
        Assert.Equal("f64 -0", IrFloat.Text(IrFloat.Of(-0.0)));
        Assert.Equal("f64 NaN", IrFloat.Text(IrFloat.Of(double.NaN)));
        Assert.Equal("f32 -Infinity", IrFloat.Text(IrFloat.Of(float.NegativeInfinity)));
        Assert.Equal("f64 1E+300", IrFloat.Text(IrFloat.Of(1e300)));
        Assert.Equal("f64 5E-324", IrFloat.Text(new IrSortValue("System.Double", 1)));
        Assert.Null(IrFloat.Text(new IrSortValue("System.Decimal", 1)));
        Assert.Throws<ArgumentNullException>(static () => IrFloat.Text(null!));
    }

    [Fact]
    public void OnlyTheTwoFloatingPointSortsHaveAWidth()
    {
        Assert.Equal(32, IrFloat.Width(new IrSort("System.Single")));
        Assert.Equal(64, IrFloat.Width(new IrSort("System.Double")));
        Assert.Null(IrFloat.Width(new IrSort("System.Decimal")));
        Assert.Null(IrFloat.Width(new IrBitVec(32)));
        Assert.Null(IrFloat.Width(new IrBitVec(64)));
        Assert.Null(IrFloat.Width(Bool));
        Assert.Throws<ArgumentNullException>(static () => IrFloat.ToSingle(null!));
        Assert.Throws<ArgumentNullException>(static () => IrFloat.ToDouble(null!));
    }

    [Fact]
    public void TheInterpretableFunctionsAreExactlyThese()
    {
        string[] operators =
        [
            "op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)", "op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)",
            "op:System.UIntPtr::op_Equality(System.UIntPtr,System.UIntPtr)", "op:System.UIntPtr::op_Inequality(System.UIntPtr,System.UIntPtr)",
        ];
        string[] operations = ["add", "sub", "mul", "div", "neg", "eq", "ne", "lt", "le", "gt", "ge"];
        string[] integers = ["i8", "u8", "i16", "u16", "char", "i32", "u32", "i64", "u64"];
        IEnumerable<string> expected = operators
            .Concat(operations.SelectMany(static o => (string[])["f32." + o, "f64." + o]))
            .Concat(["conv.f32.f64", "conv.f64.f32"])
            .Concat(integers.SelectMany(static i => (string[])[$"conv.f32.{i}", $"conv.f64.{i}"]))
            .Concat(integers.Where(static i => i is not ("i64" or "u64")).SelectMany(static i => (string[])[$"conv.{i}.f32", $"conv.{i}.f64"]));

        Assert.Equal(expected.Order(StringComparer.Ordinal), IrPureMeaning.Functions, StringComparer.Ordinal);
        Assert.Equal(integers, IrPureMeaning.IntegerCodes.Select(static c => c.Code), StringComparer.Ordinal);
        Assert.Equal([8, 8, 16, 16, 16, 32, 32, 64, 64], IrPureMeaning.IntegerCodes.Select(static c => c.Width));
        Assert.Equal([true, false, true, false, false, true, false, true, false], IrPureMeaning.IntegerCodes.Select(static c => c.Signed));
    }

    [Theory]
    [InlineData("f64.add", false, false, true)]
    [InlineData("f64.add", true, false, false)]
    [InlineData("f64.rem", false, false, false)]
    [InlineData("dec.add", false, false, false)]
    [InlineData("x87.f64.add", false, false, false)]
    [InlineData("conv.i64.f64", false, false, false)]
    [InlineData("conv.f64.i32", false, false, true)]
    [InlineData("conv.f64.i32", false, true, false)]
    [InlineData("op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)", false, true, true)]
    [InlineData("op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)", true, true, false)]
    [InlineData("op:System.String::op_Equality(string,string)", false, true, false)]
    [InlineData("delegate:abc", false, false, false)]
    public void AnApplicationIsInterpretableOnlyWithAMeaningNoRuntimeRuleAndNothingToRaise(string function, bool sensitive, bool throws, bool expected)
    {
        IrPure pure = new(new IrVar("t", Bool), throws ? [new IrPureThrow(new IrVar("o", Bool), "System.Exception")] : [], function, []) { RuntimeSensitive = sensitive };

        Assert.Equal(expected, IrPureMeaning.IsInterpretable(pure));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(static () => IrPureMeaning.IsInterpretable(null!));
        Assert.Throws<ArgumentNullException>(static () => IrPureMeaning.Evaluate(null!, []));
        Assert.Null(IrPureMeaning.Evaluate("f64.rem", [IrFloat.Of(1.0), IrFloat.Of(2.0)]));
    }

    [Fact]
    public void ArithmeticIsDotNetsOwn()
    {
        Assert.Equal(IrFloat.Of(0.1 + 0.2), Evaluate("f64.add", IrFloat.Of(0.1), IrFloat.Of(0.2)));
        Assert.Equal(IrFloat.Of(0.1f + 0.2f), Evaluate("f32.add", IrFloat.Of(0.1f), IrFloat.Of(0.2f)));
        Assert.Equal(IrFloat.Of(16777216f), Evaluate("f32.add", IrFloat.Of(16777216f), IrFloat.Of(1f)));
        Assert.Equal(IrFloat.Of(16777217.0), Evaluate("f64.add", IrFloat.Of(16777216.0), IrFloat.Of(1.0)));
        Assert.Equal(IrFloat.Of(3.0 - 0.5), Evaluate("f64.sub", IrFloat.Of(3.0), IrFloat.Of(0.5)));
        Assert.Equal(IrFloat.Of(2.5f), Evaluate("f32.sub", IrFloat.Of(3f), IrFloat.Of(0.5f)));
        Assert.Equal(IrFloat.Of(0.1 * 3.0), Evaluate("f64.mul", IrFloat.Of(0.1), IrFloat.Of(3.0)));
        Assert.Equal(IrFloat.Of(0.1f * 3f), Evaluate("f32.mul", IrFloat.Of(0.1f), IrFloat.Of(3f)));
        Assert.Equal(IrFloat.Of(1.0 / 3.0), Evaluate("f64.div", IrFloat.Of(1.0), IrFloat.Of(3.0)));
        Assert.Equal(IrFloat.Of(1f / 3f), Evaluate("f32.div", IrFloat.Of(1f), IrFloat.Of(3f)));
        Assert.Equal(IrFloat.Of(double.PositiveInfinity), Evaluate("f64.div", IrFloat.Of(1.0), IrFloat.Of(0.0)));
        Assert.Equal(IrFloat.Of(double.NaN), Evaluate("f64.div", IrFloat.Of(0.0), IrFloat.Of(0.0)));
        Assert.Equal(IrFloat.Of(-0.0), Evaluate("f64.neg", IrFloat.Of(0.0)));
        Assert.Equal(IrFloat.Of(-1.5f), Evaluate("f32.neg", IrFloat.Of(1.5f)));
        Assert.Equal(IrFloat.Of(double.NaN), Evaluate("f64.neg", IrFloat.Of(double.NaN)));
    }

    [Theory]
    [InlineData("eq", 1.0, 1.0, true)]
    [InlineData("eq", 1.0, 2.0, false)]
    [InlineData("eq", 0.0, -0.0, true)]
    [InlineData("eq", double.NaN, double.NaN, false)]
    [InlineData("ne", 1.0, 1.0, false)]
    [InlineData("ne", 1.0, 2.0, true)]
    [InlineData("ne", double.NaN, double.NaN, true)]
    [InlineData("lt", 1.0, 2.0, true)]
    [InlineData("lt", 2.0, 2.0, false)]
    [InlineData("lt", double.NaN, 2.0, false)]
    [InlineData("le", 2.0, 2.0, true)]
    [InlineData("le", 3.0, 2.0, false)]
    [InlineData("le", double.NaN, double.NaN, false)]
    [InlineData("gt", 3.0, 2.0, true)]
    [InlineData("gt", 2.0, 2.0, false)]
    [InlineData("gt", 2.0, double.NaN, false)]
    [InlineData("ge", 2.0, 2.0, true)]
    [InlineData("ge", 1.0, 2.0, false)]
    [InlineData("ge", double.NaN, double.NaN, false)]
    public void ComparisonsAreIeee(string comparison, double left, double right, bool expected)
    {
        Assert.Equal(new IrBoolValue(expected), Evaluate("f64." + comparison, IrFloat.Of(left), IrFloat.Of(right)));
        Assert.Equal(new IrBoolValue(expected), Evaluate("f32." + comparison, IrFloat.Of((float)left), IrFloat.Of((float)right)));
    }

    [Fact]
    public void ConversionsBetweenTheFormatsRoundOnce()
    {
        Assert.Equal(IrFloat.Of((double)0.1f), Evaluate("conv.f32.f64", IrFloat.Of(0.1f)));
        Assert.Equal(IrFloat.Of((float)0.1), Evaluate("conv.f64.f32", IrFloat.Of(0.1)));
        Assert.Equal(IrFloat.Of(float.PositiveInfinity), Evaluate("conv.f64.f32", IrFloat.Of(1e300)));
        Assert.Equal(IrFloat.Of(double.NaN), Evaluate("conv.f32.f64", IrFloat.Of(float.NaN)));
    }

    [Theory]
    [InlineData("i8", 8, 0xFFUL, -1.0)]
    [InlineData("u8", 8, 0xFFUL, 255.0)]
    [InlineData("i16", 16, 0x8000UL, -32768.0)]
    [InlineData("u16", 16, 0x8000UL, 32768.0)]
    [InlineData("char", 16, 0xFFFFUL, 65535.0)]
    [InlineData("i32", 32, 0xFFFF_FFFFUL, -1.0)]
    [InlineData("u32", 32, 0xFFFF_FFFFUL, 4294967295.0)]
    [InlineData("i32", 32, 16777217UL, 16777217.0)]
    public void AnIntegerOfAtMost32BitsConvertsByItsSignedness(string code, int width, ulong bits, double value)
    {
        Assert.Equal(IrFloat.Of(value), Evaluate($"conv.{code}.f64", new IrBitVecValue(width, bits)));
        Assert.Equal(IrFloat.Of((float)value), Evaluate($"conv.{code}.f32", new IrBitVecValue(width, bits)));
    }

    [Theory]
    [InlineData("i32", 1.9, 1L)]
    [InlineData("i32", -1.9, -1L)]
    [InlineData("i32", -0.5, 0L)]
    [InlineData("i32", 2147483647.9, 2147483647L)]
    [InlineData("i32", -2147483648.9, -2147483648L)]
    [InlineData("i8", -128.5, -128L)]
    [InlineData("i8", 127.5, 127L)]
    [InlineData("u8", 255.9, 255L)]
    [InlineData("u8", -0.9, 0L)]
    [InlineData("char", 65535.5, 65535L)]
    [InlineData("u32", 4294967295.5, 4294967295L)]
    [InlineData("i64", -9223372036854775808.0, long.MinValue)]
    [InlineData("i64", 9223372036854774784.0, 9223372036854774784L)]
    public void AFloatingPointValueInRangeTruncatesTowardZero(string code, double value, long expected)
    {
        int width = IrPureMeaning.IntegerCodes.Single(c => string.Equals(c.Code, code, StringComparison.Ordinal)).Width;

        Assert.Equal(IrBitVecValue.FromSigned(width, expected), Evaluate($"conv.f64.{code}", IrFloat.Of(value)));
    }

    [Fact]
    public void AnUnsigned64BitValueAboveTheSignedRangeTruncates()
    {
        Assert.Equal(new IrBitVecValue(64, 0x8000_0000_0000_0000), Evaluate("conv.f64.u64", IrFloat.Of(9223372036854775808.0)));
        Assert.Equal(new IrBitVecValue(64, 0xFFFF_FFFF_FFFF_F800), Evaluate("conv.f64.u64", IrFloat.Of(18446744073709549568.0)));
        Assert.Equal(new IrBitVecValue(32, 3), Evaluate("conv.f32.u32", IrFloat.Of(3.7f)));
        Assert.Equal(IrBitVecValue.FromSigned(16, -3), Evaluate("conv.f32.i16", IrFloat.Of(-3.7f)));
    }

    [Theory]
    [InlineData("i32", 2147483648.0)]
    [InlineData("i32", -2147483904.0)]
    [InlineData("i8", 128.0)]
    [InlineData("i8", -129.0)]
    [InlineData("u8", 256.0)]
    [InlineData("u8", -1.0)]
    [InlineData("u32", 4294967296.0)]
    [InlineData("i64", 9223372036854775808.0)]
    [InlineData("u64", 18446744073709551616.0)]
    [InlineData("u64", -1.0)]
    [InlineData("i32", double.NaN)]
    [InlineData("i32", double.PositiveInfinity)]
    [InlineData("u16", double.NegativeInfinity)]
    public void AFloatingPointValueOutOfRangeHasNoMeaning(string code, double value)
    {
        Assert.Null(Evaluate($"conv.f64.{code}", IrFloat.Of(value)));
        Assert.Null(Evaluate($"conv.f32.{code}", IrFloat.Of((float)value)));
    }

    [Fact]
    public void TheRangeIsTheTruncatedValuesNotTheArguments()
    {
        // -2147483648.5 truncates into range; -2147483649 does not. As a float both are -2^31, which is in range.
        Assert.Equal(IrBitVecValue.FromSigned(32, int.MinValue), Evaluate("conv.f64.i32", IrFloat.Of(-2147483648.5)));
        Assert.Null(Evaluate("conv.f64.i32", IrFloat.Of(-2147483649.0)));
        Assert.Equal(IrBitVecValue.FromSigned(32, int.MinValue), Evaluate("conv.f32.i32", IrFloat.Of(-2147483649f)));
        Assert.Equal(new IrBitVecValue(8, 0), Evaluate("conv.f64.u8", IrFloat.Of(-0.0)));
    }

    [Fact]
    public void PointerEqualityIsEqualityOfElements()
    {
        const string Equal = "op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr)";
        const string Unequal = "op:System.UIntPtr::op_Inequality(System.UIntPtr,System.UIntPtr)";
        IrSortValue zero = new("System.IntPtr", 0);

        Assert.Equal(new IrBoolValue(Value: true), Evaluate(Equal, zero, zero));
        Assert.Equal(new IrBoolValue(Value: false), Evaluate(Equal, zero, new IrSortValue("System.IntPtr", 4)));
        Assert.Equal(new IrBoolValue(Value: false), Evaluate(Unequal, zero, zero));
        Assert.Equal(new IrBoolValue(Value: true), Evaluate(Unequal, zero, new IrSortValue("System.UIntPtr", 0)));
    }

    [Fact]
    public void TheInterpreterComputesAnInterpretedFunctionWithoutAnOracleOrTaint()
    {
        IrProcedure procedure = IrText.Parse($"""
            proc "T::M" (%a: {Double}, %b: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %b)
              %t: {Double} = pure "f64.mul"(%s, %s)
              ret %t
            """);

        IrRun run = Run(procedure, [IrFloat.Of(1.5), IrFloat.Of(2.0)], pure: null, "f64.add", "f64.mul");

        Assert.Equal(new IrReturned(IrFloat.Of(12.25)), run.Outcome);
        Assert.Equal(IrTaint.None, run.Taint);
        Assert.Empty(run.Taint.Sources);
        Assert.False(run.Taint.Value);
    }

    [Fact]
    public void AFunctionThatIsNotInterpretedIsStillAskedAndTainted()
    {
        IrProcedure procedure = IrText.Parse($"""
            proc "T::M" (%a: {Double}, %b: {Double}) -> {Double} entry B0
            B0:
              %s: {Double} = pure "f64.add"(%a, %b)
              %t: {Double} = pure "f64.mul"(%s, %s)
              ret %t
            """);
        Answers answers = new(IrFloat.Of(7.0));

        IrRun run = Run(procedure, [IrFloat.Of(1.5), IrFloat.Of(2.0)], answers, "f64.add");

        Assert.Equal(new IrReturned(IrFloat.Of(7.0)), run.Outcome);
        (IrPure asked, ImmutableArray<IrValue> arguments) = Assert.Single(answers.Asked);
        Assert.Equal("f64.mul", asked.Function);
        Assert.Equal<IrValue>([IrFloat.Of(3.5), IrFloat.Of(3.5)], arguments);
        Assert.Equal([new CallIdentity("f64.mul")], run.Taint.Sources);
        Assert.True(run.Taint.Value);
    }

    [Fact]
    public void AnInterpretedResultIsTaintedOnlyThroughItsArguments()
    {
        IrProcedure procedure = IrText.Parse($"""
            proc "T::M" (%a: {Double}) -> {Double} entry B0
            B0:
              %r: {Double} = pure "f64.rem"(%a, %a)
              %s: {Double} = pure "f64.add"(%r, %a)
              ret %s
            """);

        IrRun run = Run(procedure, [IrFloat.Of(1.5)], new Answers(IrFloat.Of(0.25)), "f64.add");

        Assert.Equal(new IrReturned(IrFloat.Of(1.75)), run.Outcome);
        Assert.True(run.Taint.Value);
        Assert.Equal([new CallIdentity("f64.rem")], run.Taint.Sources);
    }

    [Fact]
    public void AnInterpretedOperatorRaisesNothing()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M" (%a: sort "System.IntPtr", %b: sort "System.IntPtr") -> bool entry B0
            B0:
              %c: bool = pure "op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)"(%a, %b) throws(%o: bool "System.Exception")
              br %o, B1, B2
            B1:
              throw "System.Exception"
            B2:
              ret %c
            """);
        string[] interpreted = ["op:System.IntPtr::op_Inequality(System.IntPtr,System.IntPtr)"];

        IrRun differ = Run(procedure, [new IrSortValue("System.IntPtr", 1), new IrSortValue("System.IntPtr", 2)], pure: null, interpreted);
        IrRun same = Run(procedure, [new IrSortValue("System.IntPtr", 1), new IrSortValue("System.IntPtr", 1)], pure: null, interpreted);

        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), differ.Outcome);
        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), same.Outcome);
        Assert.False(differ.Taint.Outcome);
    }

    [Fact]
    public void AConversionOutsideItsMeaningIsAskedAndTainted()
    {
        IrProcedure procedure = IrText.Parse($"""
            proc "T::M" (%a: {Double}) -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"(%a)
              ret %i
            """);
        Answers answers = new(new IrBitVecValue(32, 99));

        IrRun inside = Run(procedure, [IrFloat.Of(-7.9)], answers, "conv.f64.i32");
        IrRun outside = Run(procedure, [IrFloat.Of(1e10)], answers, "conv.f64.i32");

        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, -7)), inside.Outcome);
        Assert.Empty(inside.Taint.Sources);
        Assert.Equal(new IrReturned(new IrBitVecValue(32, 99)), outside.Outcome);
        Assert.Equal([new CallIdentity("conv.f64.i32")], outside.Taint.Sources);
        Assert.Single(answers.Asked);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Run(procedure, [IrFloat.Of(1e10)], pure: null, "conv.f64.i32"));
        Assert.Contains("conv.f64.i32", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALadderStepComparesWhatItRefinedByValue()
    {
        LadderStep step = new(ProofMethod.Bounded, RungOutcome.Proved, "d") { Refined = ["f64.add", "f64.mul"] };
        LadderStep same = new(ProofMethod.Bounded, RungOutcome.Proved, "d") { Refined = ["f64.add", "f64.mul"] };

        Assert.Equal(step, same);
        Assert.Equal(step.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(step, same with { Refined = ["f64.add"] });
        Assert.NotEqual(step, same with { Refined = [] });
        Assert.NotEqual(step, same with { Rung = ProofMethod.KInduction });
        Assert.NotEqual(step, same with { Outcome = RungOutcome.Refuted });
        Assert.NotEqual(step, same with { Detail = "e" });
        Assert.NotEqual(step, same with { Mode = ChcMode.Integers });
        Assert.NotEqual(step, same with { Solver = new SolverUse("cvc5", "1") });
        Assert.NotEqual(step.GetHashCode(), (same with { Refined = ["f64.add"] }).GetHashCode());
        Assert.False(step.Equals(Null.Of<LadderStep>()));
        Assert.Empty(new LadderStep(ProofMethod.Bounded, RungOutcome.Proved, "d").Refined);
    }

    private static IrValue? Evaluate(string function, params IrValue[] arguments) => IrPureMeaning.Evaluate(function, [.. arguments]);

    private static IrRun Run(IrProcedure procedure, ImmutableArray<IrValue> inputs, IPureOracle? pure, params string[] interpreted) =>
        IrInterpreter.Run(procedure, new IrInputs(inputs), new NoCalls(), 100, static _ => false, pure, interpreted.ToHashSet(StringComparer.Ordinal));

    /// <summary>Answers every pure application with one value and no exception, and records what it was asked.</summary>
    private sealed class Answers(IrValue value) : IPureOracle
    {
        public List<(IrPure Pure, ImmutableArray<IrValue> Arguments)> Asked { get; } = [];

        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
        {
            Asked.Add((pure, arguments));
            return new IrPureResult(value, [.. pure.Throws.Select(static _ => false)]);
        }
    }

    private sealed class NoCalls : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts) =>
            new(Value: null, Threw: false);
    }
}
