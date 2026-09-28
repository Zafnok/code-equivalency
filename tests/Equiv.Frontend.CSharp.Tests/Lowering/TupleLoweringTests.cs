using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// A value tuple of two or three <c>bool</c> or integral elements is an <see cref="IrTuple"/> sort: a literal is
/// <c>tuple.new</c> of its elements and an element read <c>tuple.item</c> of its position, whatever it is named (ticket
/// P2-027).
/// </summary>
public sealed class TupleLoweringTests
{
    [Fact]
    public void ATupleLiteralIsTupleNewOfItsElements()
    {
        IrProcedure procedure = Method("static (int, bool) M(int a, bool b) => (a, b);");

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal(IrTuple.New, pure.Function);
        Assert.Equal<IrVar>([procedure.Parameters[0].Var, procedure.Parameters[1].Var], pure.Args);
        Assert.Equal(new IrSort("tuple(bv32,bool)"), pure.Target.Type);
        Assert.Equal(pure.Target.Type, procedure.ReturnType);
        Assert.Empty(pure.Throws);
        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData("p.Y")]
    [InlineData("p.Item2")]
    public void ANamedElementAndItsItemNameAreOneRead(string read)
    {
        IrProcedure procedure = Method($"static long M((int X, long Y, byte Z) p) => {read};");

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal(IrTuple.Item(2), pure.Function);
        Assert.Equal<IrVar>([procedure.Parameters[0].Var], pure.Args);
        Assert.Equal(new IrBitVec(64), pure.Target.Type);
        Assert.Equal(new IrSort("tuple(bv32,bv64,bv8)"), procedure.Parameters[0].Var.Type);
        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData("static int M((int X, int Y) p) { p.X = 1; return p.X; }")]
    [InlineData("static int M((int X, int Y) p) { p.Item1 += 1; return p.X; }")]
    [InlineData("static int M((int X, int Y) p) { p.X++; return p.X; }")]
    public void WritingAnElementIsOpaque(string members) =>
        Assert.Equal("Tuple", Assert.Single(Opaques(Method(members))).Reason);

    [Theory]
    [InlineData("static (string, int) M(string a, int b) => (a, b);")]
    [InlineData("static (int, int, int, int) M(int a) => (a, a, a, a);")]
    [InlineData("static (int, (int, int)) M(int a) => (a, (a, a));")]
    public void ATupleOutsideTheSizeGuardIsOpaque(string members) =>
        Assert.Contains(Opaques(Method(members)), static o => string.Equals(o.Reason, "Tuple", StringComparison.Ordinal));

    [Fact]
    public void AnElementOfATupleOutsideTheSizeGuardIsStillItsFieldMap()
    {
        IrProcedure procedure = Method("static string M((string A, int B) p) => p.A;");

        Assert.Empty(Pures(procedure));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal));
    }

    [Fact]
    public void TupleEqualityStaysOpaque() =>
        Assert.Equal("TupleBinaryOperator", Assert.Single(Opaques(Method("static bool M((int, int) a, (int, int) b) => a == b;"))).Reason);

    private static ImmutableArray<IrPure> Pures(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>()];
}
