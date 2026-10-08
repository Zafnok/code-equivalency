using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// VERIFICATION-MODEL.md section 7's soundness obligation with refinement on (ADR 0053; ticket P1-030). The IR generator's
/// own pure functions have no meaning, so no pair of the main harness is ever refined; this one generates straight-line
/// <c>double</c> procedures over the interpretable functions, and a second procedure one edit away. Whatever the verdict,
/// it is held to .NET's arithmetic: an Equivalent pair agrees on every input tried, edge values first, and a Divergent's
/// model gives different results when both sides are run on it.
/// </summary>
public sealed class RefinementSoundnessTests
{
    private const string Double = "sort \"System.Double\"";

    private static readonly VerificationOptions Options = new(3, 10_000, []) { RefineTimeouts = false };

    private static readonly double[] Edges =
    [
        0.0, -0.0, 1.0, -1.0, 2.0, 0.5, 3.0, 0.1, 1e16, 9007199254740993.0, 2147483648.0, -2147483649.0, 1e300, -1e300,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, double.MaxValue, 16777217.0,
    ];

    private static readonly HashSet<string> Meanings = IrPureMeaning.Functions.ToHashSet(StringComparer.Ordinal);

    private static readonly Gen<double> Input = Gen.Frequency((3, Gen.OneOfConst(Edges)), (1, Gen.Double[-100, 100]), (1, Gen.ULong.Select(BitConverter.UInt64BitsToDouble)));

    /// <summary>One step: a function of one or two earlier values (the parameters are values 0 and 1), or a constant.</summary>
    private static readonly Gen<Step> Steps = Gen.Frequency(
        (4, Gen.Select(Gen.Const("f64.add"), Gen.Int[0, 5], Gen.Int[0, 5], static (f, l, r) => new Step(f, l, r, 0.0))),
        (4, Gen.Select(Gen.Const("f64.sub"), Gen.Int[0, 5], Gen.Int[0, 5], static (f, l, r) => new Step(f, l, r, 0.0))),
        (1, Gen.Select(Gen.Const("f64.mul"), Gen.Int[0, 5], Gen.Int[0, 5], static (f, l, r) => new Step(f, l, r, 0.0))),
        (2, Gen.Select(Gen.OneOfConst("f64.neg", "narrow", "truncate"), Gen.Int[0, 5], static (f, l) => new Step(f, l, 0, 0.0))),
        (2, Gen.OneOfConst(0.0, -0.0, 1.0, 2.0, 0.5).Select(static c => new Step("const", 0, 0, c))));

    private static readonly Gen<Program> Programs = Gen.Select(
        Steps.Array[1, 4],
        Gen.OneOfConst("ret", "f64.eq", "f64.ne", "f64.lt", "f64.le", "f64.gt", "f64.ge"),
        Gen.Int[0, 5],
        Gen.Int[0, 5],
        static (steps, exit, l, r) => new Program([.. steps], exit, l, r));

    /// <summary>A program and one a single edit away: a step, the exit, or an operand of the exit replaced.</summary>
    private static readonly Gen<(Program Old, Program New)> Pairs = Gen.Select(
        Programs,
        Programs,
        Gen.Int[0, 5],
        static (old, other, at) => (old, at switch
        {
            0 => old with { Exit = other.Exit },
            1 => old with { Left = other.Left },
            2 => old with { Right = other.Right },
            _ => old with { Steps = old.Steps.SetItem((at - 3) % old.Steps.Length, other.Steps[0]) },
        }));

    [Fact]
    public void AProgramIsEquivalentToItself() =>
        Programs.Sample(
            static program => Assert.IsType<Equivalent>(new Z3Backend().Verify(IrText.Parse(program.Text()), IrText.Parse(program.Text()), Options)),
            iter: 20,
            print: static p => p.Text());

    [Fact]
    public void ARefinedVerdictHoldsOnDotNetArithmetic()
    {
        int decided = 0;
        Pairs.Sample(
            pair =>
            {
                IrProcedure old = IrText.Parse(pair.Old.Text());
                IrProcedure @new = IrText.Parse(pair.New.Text());
                Verdict verdict = new Z3Backend().Verify(old, @new, Options);
                if (verdict is Divergent { Counterexample: { Old.Taint.Sources.IsEmpty: true, New.Taint.Sources.IsEmpty: true } } divergent)
                {
                    // A run that reached a conversion outside its meaning took the model's answer for it, not this test's.
                    IrInputs inputs = divergent.Counterexample.Inputs;
                    Assert.NotEqual(Run(old, inputs), Run(@new, inputs));
                    Assert.Equal(divergent.Counterexample.Old.Outcome, Run(old, inputs));
                    Assert.Equal(divergent.Counterexample.New.Outcome, Run(@new, inputs));
                }
                else if (verdict is Equivalent)
                {
                    foreach (IrInputs inputs in Edges.SelectMany(static a => Edges.Select(b => new IrInputs([IrFloat.Of(a), IrFloat.Of(b)]))))
                    {
                        Assert.Equal(Run(old, inputs), Run(@new, inputs));
                    }

                    Input.Array[2].Sample(drawn => Assert.Equal(Run(old, Inputs(drawn)), Run(@new, Inputs(drawn))), iter: 200, threads: 1);
                }

                if (verdict.Ladder.Any(static s => !s.Refined.IsEmpty))
                {
                    Interlocked.Increment(ref decided);
                }
            },
            iter: 60,
            print: static p => p.Old.Text() + "\n---\n" + p.New.Text());

        // The property is about refined verdicts, so some must have been.
        Assert.True(decided >= 5, $"only {decided.ToString(CultureInfo.InvariantCulture)} of 60 pairs were decided after refinement");
    }

    private static IrInputs Inputs(double[] drawn) => new([IrFloat.Of(drawn[0]), IrFloat.Of(drawn[1])]);

    /// <summary>
    /// The outcome of <paramref name="procedure"/> under .NET's arithmetic. A conversion outside its meaning is answered by
    /// a fixed function of its argument, which is one of the functions the shared one ranges over.
    /// </summary>
    private static IrOutcome Run(IrProcedure procedure, IrInputs inputs) =>
        IrInterpreter.Run(procedure, inputs, IrGenOracle.Instance, 1000, taint: null, IrGenOracle.Instance, Meanings).Outcome;

    private sealed record Step(string Function, int Left, int Right, double Constant);

    private sealed record Program(ImmutableArray<Step> Steps, string Exit, int Left, int Right)
    {
        /// <summary>
        /// The IR text. Value <c>i</c> is <c>%v&lt;i&gt;</c>: 0 and 1 are the parameters, and step <c>s</c> defines value
        /// <c>s + 2</c> from values before it, an operand index taken modulo how many there are.
        /// </summary>
        public string Text()
        {
            StringBuilder text = new();
            bool flag = !string.Equals(Exit, "ret", StringComparison.Ordinal);
            text.Append(CultureInfo.InvariantCulture, $"proc \"T::M(double,double)\" (%v0: {Double}, %v1: {Double}) -> {(flag ? "bool" : Double)} entry B0\nB0:\n");
            for (int i = 0; i < Steps.Length; i++)
            {
                Step step = Steps[i];
                int defined = i + 2;
                string target = $"%v{defined.ToString(CultureInfo.InvariantCulture)}";
                string left = $"%v{(step.Left % defined).ToString(CultureInfo.InvariantCulture)}";
                string right = $"%v{(step.Right % defined).ToString(CultureInfo.InvariantCulture)}";
                text.Append(step.Function switch
                {
                    "const" => $"  {target}: {Double} = const {Double} {IrFloat.Of(step.Constant).Id.ToString(CultureInfo.InvariantCulture)}\n",
                    "f64.neg" => $"  {target}: {Double} = pure \"f64.neg\"({left})\n",
                    "narrow" => $"  {target}n: sort \"System.Single\" = pure \"conv.f64.f32\"({left})\n  {target}: {Double} = pure \"conv.f32.f64\"({target}n)\n",
                    "truncate" => $"  {target}n: bv32 = pure \"conv.f64.i32\"({left})\n  {target}: {Double} = pure \"conv.i32.f64\"({target}n)\n",
                    var binary => $"  {target}: {Double} = pure \"{binary}\"({left}, {right})\n",
                });
            }

            int count = Steps.Length + 2;
            string first = $"%v{(Left % count).ToString(CultureInfo.InvariantCulture)}";
            string second = $"%v{(Right % count).ToString(CultureInfo.InvariantCulture)}";
            return flag
                ? text.Append(CultureInfo.InvariantCulture, $"  %c: bool = pure \"{Exit}\"({first}, {second})\n  ret %c").ToString()
                : text.Append(CultureInfo.InvariantCulture, $"  ret {first}").ToString();
        }
    }
}
