using System.Collections.Immutable;

using CsCheck;

using Equiv.Core.Ir;

using Equiv.Verify.Z3.Refinement;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The Z3 term of every interpretable pure function agrees with <see cref="IrPureMeaning.Evaluate"/>, which is .NET's own
/// arithmetic (ADR 0053 decision 3; ticket P1-030): a refined query that Z3 answers and the replay that checks it mean the
/// same function, bit for bit.
/// </summary>
public sealed class InterpretedPureTests
{
    private static readonly double[] Doubles =
    [
        0.0, -0.0, 1.0, -1.0, 2.0, 0.5, -0.5, 0.1, 255.5, 256.0, -128.5, 65535.9, 65536.0, 16777217.0,
        2147483647.0, 2147483647.5, 2147483648.0, -2147483648.0, -2147483648.5, -2147483649.0, 4294967295.5, 4294967296.0,
        9223372036854774784.0, 9223372036854775808.0, -9223372036854775808.0, 18446744073709549568.0, 18446744073709551616.0,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon, 2.2250738585072009E-308,
        2.2250738585072014E-308, double.MaxValue, double.MinValue, float.MaxValue, 3.4028235677973366E+38, 1e-320,
    ];

    private static readonly float[] Singles =
    [
        0f, -0f, 1f, -1f, 2f, 0.5f, -0.5f, 0.1f, 255.5f, 256f, 65536f, 16777216f, 2147483520f, 2147483648f, -2147483648f,
        4294967040f, 4294967296f, 9.223372E+18f, 1.8446744E+19f, float.NaN, float.PositiveInfinity, float.NegativeInfinity,
        float.Epsilon, -float.Epsilon, 1.17549421E-38f, 1.17549435E-38f, float.MaxValue, float.MinValue,
    ];

    private static readonly Gen<double> AnyDouble = Gen.Frequency(
        (3, Gen.OneOfConst(Doubles)),
        (2, Gen.ULong.Select(BitConverter.UInt64BitsToDouble)),
        (1, Gen.Double[-70000, 70000]));

    private static readonly Gen<float> AnySingle = Gen.Frequency(
        (3, Gen.OneOfConst(Singles)),
        (2, Gen.UInt.Select(BitConverter.UInt32BitsToSingle)),
        (1, Gen.Single[-70000, 70000]));

    private static readonly Gen<(string Function, ImmutableArray<IrValue> Arguments)> Applications =
        Gen.OneOfConst([.. IrPureMeaning.Functions.Where(static f => !IsOperator(f))]).SelectMany(static f => Arguments(f).Select(a => (f, a)));

    [Fact]
    public void EveryInterpretableFunctionHasATerm() =>
        Assert.All(IrPureMeaning.Functions, static f => Assert.True(InterpretedPure.Has(f), f));

    [Fact]
    public void AFunctionWithoutAMeaningHasNoTerm()
    {
        Assert.False(InterpretedPure.Has("f64.rem"));
        Assert.False(InterpretedPure.Has("conv.i64.f64"));
        Assert.False(InterpretedPure.Has("x87.f64.add"));
    }

    [Fact]
    public void Interpreter_AgreesWithZ3OnEveryInterpretedFunction()
    {
        using Context context = new();
        Applications.Sample(
            application => { AssertAgrees(context, application.Function, application.Arguments); },
            iter: 20_000,
            threads: 1,
            print: static a => $"{a.Function}({string.Join(", ", a.Arguments)})");
    }

    /// <summary>
    /// An operator's operands are elements of an uninterpreted sort, which no rewriting decides, so the solver is asked:
    /// with the sort's literals distinct, the term cannot differ from what the interpreter computes.
    /// </summary>
    [Fact]
    public void Interpreter_AgreesWithZ3OnPointerEquality()
    {
        foreach (string function in IrPureMeaning.Functions.Where(IsOperator))
        {
            string sort = function.Contains("UIntPtr", StringComparison.Ordinal) ? "System.UIntPtr" : "System.IntPtr";
            foreach ((int left, int right) in ((int, int)[])[(0, 0), (0, 1), (1, 0), (7, 7)])
            {
                using Context context = new();
                SortMapper sorts = new(context, floats: true);
                ImmutableArray<IrValue> arguments = [new IrSortValue(sort, left), new IrSortValue(sort, right)];
                Expr term = InterpretedPure.Term(context, function, [.. arguments.Select(sorts.Literal)], static () => throw new InvalidOperationException("total"));
                using Solver solver = context.MkSolver();
                solver.Add(sorts.Distinctness());
                solver.Add(context.MkNot(context.MkEq(term, sorts.Literal(IrPureMeaning.Evaluate(function, arguments)!))));

                Assert.Equal(Status.UNSATISFIABLE, solver.Check());
                Assert.Equal(new IrBoolValue((left == right) == function.Contains("op_Equality", StringComparison.Ordinal)), IrPureMeaning.Evaluate(function, arguments));
            }
        }
    }

    /// <summary>Every function on every pair of edge values, so that no edge depends on what the generator draws.</summary>
    [Fact]
    public void Interpreter_AgreesWithZ3OnEveryEdgeValue()
    {
        using Context context = new();
        foreach (string function in IrPureMeaning.Functions.Where(static f => !IsOperator(f)))
        {
            string[] parts = function.Split('.');
            IrValue[] operands = parts[0] is "f32" || (parts[0] is "conv" && parts[1] is "f32")
                ? [.. Singles.Select(static s => (IrValue)IrFloat.Of(s))]
                : parts[0] is "f64" || parts[1] is "f64"
                    ? [.. Doubles.Select(static d => (IrValue)IrFloat.Of(d))]
                    : [.. Integers(parts[1])];
            bool binary = parts[0] is not "conv" && parts[1] is not "neg";
            foreach (IrValue left in operands)
            {
                foreach (IrValue right in binary ? operands : [left])
                {
                    AssertAgrees(context, function, binary ? [left, right] : [left]);
                }
            }
        }
    }

    private static void AssertAgrees(Context context, string function, ImmutableArray<IrValue> arguments)
    {
        SortMapper sorts = new(context, floats: true);
        IrValue? expected = IrPureMeaning.Evaluate(function, arguments);
        Expr fallback = context.MkConst("fallback", sorts.Sort(ResultType(function)));
        Expr term = InterpretedPure.Term(context, function, [.. arguments.Select(sorts.Literal)], () => fallback).Simplify();

        if (expected is null)
        {
            Assert.Equal(fallback, term);
        }
        else
        {
            Assert.Equal(ResultType(function), expected.Type);
            Assert.NotEqual(fallback, term);
            Assert.Equal(expected, new ModelDecoder.Values().Decode(term, expected.Type));
        }
    }

    private static Gen<ImmutableArray<IrValue>> Arguments(string function)
    {
        string[] parts = function.Split('.');
        Gen<IrValue> single = AnySingle.Select(static s => (IrValue)IrFloat.Of(s));
        Gen<IrValue> @double = AnyDouble.Select(static d => (IrValue)IrFloat.Of(d));
        return parts switch
        {
            ["conv", "f32", _] => single.Select(static a => ImmutableArray.Create(a)),
            ["conv", "f64", _] => @double.Select(static a => ImmutableArray.Create(a)),
            ["conv", var code, _] => Gen.OneOfConst(Integers(code)).Select(static a => ImmutableArray.Create(a)),
            ["f32", "neg"] => single.Select(static a => ImmutableArray.Create(a)),
            ["f64", "neg"] => @double.Select(static a => ImmutableArray.Create(a)),
            ["f32", _] => single.Array[2].Select(static a => a.ToImmutableArray()),
            _ => @double.Array[2].Select(static a => a.ToImmutableArray()),
        };
    }

    /// <summary>Edge values of the integer type <paramref name="code"/> names: its least and greatest, zero and their neighbours.</summary>
    private static IrValue[] Integers(string code)
    {
        (_, int width, _) = IrPureMeaning.IntegerCodes.Single(c => string.Equals(c.Code, code, StringComparison.Ordinal));
        ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        ulong top = 1UL << (width - 1);
        ulong[] bits = [0, 1, 2, 3, 100, top - 1, top, top + 1, mask - 1, mask, 16777217 & mask, 0x55555555_55555555 & mask];
        return [.. bits.Select(b => (IrValue)new IrBitVecValue(width, b))];
    }

    private static bool IsOperator(string function) => function.StartsWith("op:", StringComparison.Ordinal);

    private static IrType ResultType(string function) => function.Split('.') switch
    {
        ["conv", _, "f32"] => IrFloat.Binary32,
        ["conv", _, "f64"] => IrFloat.Binary64,
        ["conv", _, var code] => new IrBitVec(IrPureMeaning.IntegerCodes.Single(c => string.Equals(c.Code, code, StringComparison.Ordinal)).Width),
        [_, "eq" or "ne" or "lt" or "le" or "gt" or "ge"] => new IrBool(),
        ["f32", _] => IrFloat.Binary32,
        _ => IrFloat.Binary64,
    };
}
