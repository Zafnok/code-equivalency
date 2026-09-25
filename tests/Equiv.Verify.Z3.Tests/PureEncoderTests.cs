using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Pure functions in the product (ADR 0025; ticket M4-002 criterion 5): one Z3 function per name and one Bool function
/// per exception flag, shared by both sides unless the function is runtime-sensitive, and replayed from the model with
/// taint, so a divergence that depends on how the solver interprets one is Unknown(Abstraction) (ADR 0026).
/// </summary>
public sealed class PureEncoderTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void UnchangedArithmeticMovedIntoALocalIsEquivalent()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(double,double)" (%a: sort "System.Double", %b: sort "System.Double") -> bool entry B0
            B0:
              %s: sort "System.Double" = pure "f64.mul"(%a, %b)
              %t: sort "System.Double" = pure "f64.sub"(%s, %a)
              %c: bool = pure "f64.lt"(%t, %b)
              ret %c
            """,
            """
            proc "T::M(double,double)" (%x: sort "System.Double", %y: sort "System.Double") -> bool entry B0
            B0:
              %gross: sort "System.Double" = pure "f64.mul"(%x, %y)
              %net: sort "System.Double" = pure "f64.sub"(%gross, %x)
              %c: bool = pure "f64.lt"(%net, %y)
              ret %c
            """);

        Assert.IsType<Equivalent>(verdict);
    }

    [Fact]
    public void APureMovedPastACallIsStillEquivalent()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(decimal)" (%a: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              %s: sort "System.Decimal" = pure "dec.neg"(%a)
              call "Log::Write()"()
              ret %s
            """,
            """
            proc "T::M(decimal)" (%a: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              call "Log::Write()"()
              %s: sort "System.Decimal" = pure "dec.neg"(%a)
              ret %s
            """);

        Assert.IsType<Equivalent>(verdict);
    }

    [Fact]
    public void SwappedOperandsAreUnknownAbstraction()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(double,double)" (%a: sort "System.Double", %b: sort "System.Double") -> sort "System.Double" entry B0
            B0:
              %s: sort "System.Double" = pure "f64.add"(%a, %b)
              ret %s
            """,
            """
            proc "T::M(double,double)" (%a: sort "System.Double", %b: sort "System.Double") -> sort "System.Double" entry B0
            B0:
              %s: sort "System.Double" = pure "f64.add"(%b, %a)
              ret %s
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal("the divergence depends on f64.add", unknown.Detail);
        Counterexample candidate = Assert.IsType<Counterexample>(unknown.Candidate);
        Assert.NotEqual(candidate.Old.Outcome, candidate.New.Outcome);
    }

    [Fact]
    public void AnUntaintedDivergenceBesideAPureIsDivergent()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(decimal)" (%a: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              %s: sort "System.Decimal" = pure "dec.neg"(%a)
              call "Log::Write()"()
              ret %s
            """,
            """
            proc "T::M(decimal)" (%a: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              %s: sort "System.Decimal" = pure "dec.neg"(%a)
              ret %s
            """);

        Assert.IsType<Divergent>(verdict);
    }

    [Fact]
    public void AFlagDivergenceReplaysThroughTheModelsFlagFunction()
    {
        Verdict verdict = Verify(
            """
            proc "T::M(decimal,decimal)" (%a: sort "System.Decimal", %b: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              %q: sort "System.Decimal" = pure "dec.mul"(%a, %b) throws(%o: bool "System.OverflowException")
              br %o, B1, B2
            B1:
              throw "System.OverflowException"
            B2:
              ret %q
            """,
            """
            proc "T::M(decimal,decimal)" (%a: sort "System.Decimal", %b: sort "System.Decimal") -> sort "System.Decimal" entry B0
            B0:
              %q: sort "System.Decimal" = pure "dec.mul"(%a, %b) throws(%o: bool "System.OverflowException")
              br %o, B1, B2
            B1:
              %z: sort "System.Decimal" = const sort "System.Decimal" 0
              ret %z
            B2:
              ret %q
            """);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Counterexample candidate = Assert.IsType<Counterexample>(unknown.Candidate);
        Assert.Equal(new IrThrew("System.OverflowException"), candidate.Old.Outcome);
        Assert.True(candidate.Old.Taint.Outcome);
    }

    [Fact]
    public void FloatToIntConversionIsSideSpecific()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(
            """
            proc "T::M(double)" (%a: sort "System.Double") -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"!(%a)
              ret %i
            ---
            proc "T::M(double)" (%a: sort "System.Double") -> bv32 entry B0
            B0:
              %i: bv32 = pure "conv.f64.i32"!(%a)
              ret %i
            """);

        string dump = Dump(old, @new);

        Assert.Contains("|pure:old.conv.f64.i32(sort<System.Double>)->bv32|", dump, StringComparison.Ordinal);
        Assert.Contains("|pure:new.conv.f64.i32(sort<System.Double>)->bv32|", dump, StringComparison.Ordinal);
        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(old, @new, Options));
        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
    }

    [Fact]
    public void AFunctionOneSideMarksRuntimeSensitiveIsSideSpecificOnBothSides()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(
            """
            proc "T::M(double)" (%a: sort "System.Double") -> sort "System.Double" entry B0
            B0:
              %s: sort "System.Double" = pure "f64.add"!(%a, %a) throws(%o: bool "E")
              ret %s
            ---
            proc "T::M(double)" (%a: sort "System.Double") -> sort "System.Double" entry B0
            B0:
              %s: sort "System.Double" = pure "f64.add"(%a, %a) throws(%o: bool "E")
              ret %s
            """);

        string dump = Dump(old, @new);

        Assert.Contains("|pure:old.f64.add(sort<System.Double>,sort<System.Double>)->sort<System.Double>|", dump, StringComparison.Ordinal);
        Assert.Contains("|pure:new.f64.add(sort<System.Double>,sort<System.Double>)->sort<System.Double>|", dump, StringComparison.Ordinal);
        Assert.Contains("|pure.threw:new.f64.add(sort<System.Double>,sort<System.Double>):E|", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("|pure:f64.add", dump, StringComparison.Ordinal);
        Assert.IsNotType<Equivalent>(new Z3Backend().Verify(old, @new, Options));
    }

    private static Verdict Verify(string old, string @new) =>
        new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);

    private static string Dump(IrProcedure old, IrProcedure @new)
    {
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, old, @new, []);
        StringBuilder text = new();
        foreach (BoolExpr assertion in encoding.Assertions)
        {
            text.Append(assertion).Append('\n');
        }

        return text.ToString();
    }
}
