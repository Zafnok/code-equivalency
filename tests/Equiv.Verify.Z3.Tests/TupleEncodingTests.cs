using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Tuples (ticket P2-027): <c>tuple.new</c> and <c>tuple.item</c> are pure functions made each other's inverse by ground
/// facts at each application, so a read of a literal is its element and two tuples with equal elements are equal.
/// </summary>
public sealed class TupleEncodingTests
{
    private const string Pair = "sort \"tuple(bv32,bv32)\"";

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void AnElementOfALiteralIsTheElementItWasBuiltFrom()
    {
        Verdict verdict = Verify(
            $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %t: {Pair} = pure "tuple.new"(%a, %b)
              %y: bv32 = pure "tuple.item2"(%t)
              ret %y
            """,
            """
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              ret %b
            """);

        Assert.IsType<Equivalent>(verdict);
    }

    [Fact]
    public void ATupleIsTheLiteralOfItsOwnElements()
    {
        Verdict verdict = Verify(
            $"""
            proc "T::M((int,int))" (%p: {Pair}) -> {Pair} entry B0
            B0:
              ret %p
            """,
            $"""
            proc "T::M((int,int))" (%p: {Pair}) -> {Pair} entry B0
            B0:
              %x: bv32 = pure "tuple.item1"(%p)
              %y: bv32 = pure "tuple.item2"(%p)
              %t: {Pair} = pure "tuple.new"(%x, %y)
              ret %t
            """);

        Assert.IsType<Equivalent>(verdict);
    }

    [Fact]
    public void ReorderedElementReadsAreEquivalent() =>
        Assert.IsType<Equivalent>(Verify(Sum("%x, %y"), Sum("%y, %x")));

    [Fact]
    public void SwappingTheElementsOfASubtractionIsNeverEquivalent() =>
        Assert.IsNotType<Equivalent>(Verify(Difference("tuple.item1", "tuple.item2"), Difference("tuple.item2", "tuple.item1")));

    [Fact]
    public void ALiteralWithSwappedElementsIsNeverEquivalent()
    {
        Verdict verdict = Verify(
            $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> {Pair} entry B0
            B0:
              %t: {Pair} = pure "tuple.new"(%a, %b)
              ret %t
            """,
            $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> {Pair} entry B0
            B0:
              %t: {Pair} = pure "tuple.new"(%b, %a)
              ret %t
            """);

        Assert.IsNotType<Equivalent>(verdict);
    }

    [Fact]
    public void EachApplicationStatesItsInverse()
    {
        string dump = Dump(Sum("%x, %y"));

        Assert.Contains("(= (|pure:tuple.new(bv32,bv32)->sort<tuple(bv32,bv32)>|", dump, StringComparison.Ordinal);
        Assert.Contains("(|pure:tuple.item1(sort<tuple(bv32,bv32)>)->bv32| in.p)", dump, StringComparison.Ordinal);
        Assert.Contains("(|pure:tuple.item2(sort<tuple(bv32,bv32)>)->bv32| in.p)", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void ALiteralStatesEachOfItsElements()
    {
        string dump = Dump(
            $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> {Pair} entry B0
            B0:
              %t: {Pair} = pure "tuple.new"(%a, %b)
              ret %t
            """);

        const string Literal = "(|pure:tuple.new(bv32,bv32)->sort<tuple(bv32,bv32)>| in.a in.b)";
        Assert.Contains($"(= (|pure:tuple.item1(sort<tuple(bv32,bv32)>)->bv32| {Literal}) in.a)", dump, StringComparison.Ordinal);
        Assert.Contains($"(= (|pure:tuple.item2(sort<tuple(bv32,bv32)>)->bv32| {Literal}) in.b)", dump, StringComparison.Ordinal);
    }

    private static string Sum(string operands) =>
        $"""
        proc "T::M((int,int))" (%p: {Pair}) -> bv32 entry B0
        B0:
          %x: bv32 = pure "tuple.item1"(%p)
          %y: bv32 = pure "tuple.item2"(%p)
          %s: bv32 = add {operands}
          ret %s
        """;

    private static string Difference(string first, string second) =>
        $"""
        proc "T::M((int,int))" (%p: {Pair}) -> bv32 entry B0
        B0:
          %x: bv32 = pure "{first}"(%p)
          %y: bv32 = pure "{second}"(%p)
          %s: bv32 = sub %x, %y
          ret %s
        """;

    private static Verdict Verify(string old, string @new) =>
        new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);

    private static string Dump(string procedure)
    {
        using Context context = new();
        IrProcedure parsed = IrText.Parse(procedure);
        ProductEncoding encoding = ProductEncoder.Encode(context, parsed, parsed, []);
        StringBuilder text = new();
        foreach (BoolExpr assertion in encoding.Assertions)
        {
            text.AppendJoin(' ', assertion.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Append('\n');
        }

        return text.ToString();
    }
}
