using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;
using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// An anonymous object that is itself an argument of a call is a closed call (ADR 0041, clarified for ticket P2-088) to
/// <c>{names}::.ctor(types)</c>, the property names and types in declaration order, of the property values in that order.
/// So two sides that build the same object from equal values agree. Anywhere else the creation stays opaque with reason
/// <c>AnonymousObjectCreation</c>.
/// </summary>
public sealed class AnonymousObjectLoweringTests
{
    private const string F = "static void F(object o) { } ";

    /// <summary>Criterion 2: the creation is one closed call of the property values, with no opaque.</summary>
    [Fact]
    public Task AnAnonymousObjectPassedToACallIsAClosedCall()
    {
        IrProcedure procedure = Method(F + "static void M(int x, string s) { F(new { X = x, Name = s }); }");

        Assert.Empty(Opaques(procedure));
        return Verify(IrText.Dump(procedure));
    }

    /// <summary>The callee names the properties and their types in declaration order, and takes the values in that order.</summary>
    [Fact]
    public void TheCalleeNamesThePropertiesInDeclarationOrder()
    {
        IrProcedure procedure = Method("static void G<T>(T self) { } static void M(int x, string s) { G(new { X = x, Name = s }); }");

        IrCall creation = Calls(procedure)[0];
        Assert.Equal("{X,Name}::.ctor(int,string)", creation.Callee.Value);
        Assert.True(creation.Closed);
        Assert.False(creation.Callee.External);
        Assert.Equal(["x", "s"], creation.Args.Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.Equal(creation.Target, Calls(procedure)[1].Args[0]);
    }

    /// <summary>A property named by the expression it is read from is the property written out.</summary>
    [Theory]
    [InlineData("static void M(int x) { F(new { x }); }", "static void M(int x) { F(new { x = x }); }")]
    [InlineData("static void G<T>(T self) { } static void M(int x) { G(new { x, Y = 1 }); }", "static void G<T>(T self) { } static void M(int x) { G(new { x = x, Y = 1 }); }")]
    public void TwoSpellingsOfOneObjectLowerAlike(string inferred, string written)
    {
        IrProcedure procedure = Method(F + inferred);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(IrText.Dump(Method(F + written)), IrText.Dump(procedure));
    }

    /// <summary>Other names, another order or other types are another object.</summary>
    [Theory]
    [InlineData("new { X = x, Y = y }", "{X,Y}::.ctor(int,int)")]
    [InlineData("new { Y = y, X = x }", "{Y,X}::.ctor(int,int)")]
    [InlineData("new { X = x, Z = y }", "{X,Z}::.ctor(int,int)")]
    [InlineData("new { X = (long)x, Y = y }", "{X,Y}::.ctor(long,int)")]
    [InlineData("new { }", "{}::.ctor()")]
    public void TheCalleeIsTheObjectsShape(string creation, string callee)
    {
        IrProcedure procedure = Method(F + "static void M(int x, int y) { F(" + creation + "); }");

        Assert.Equal(callee, Calls(procedure)[0].Callee.Value);
    }

    /// <summary>The values are evaluated in declaration order, before the creation.</summary>
    [Fact]
    public void ThePropertyValuesAreEvaluatedInOrder()
    {
        IrProcedure procedure = Method(F + "static int A() => 1; static int B() => 2; static void M() { F(new { P = A(), Q = B() }); }");

        Assert.Equal(["C::A()", "C::B()", "{P,Q}::.ctor(int,int)", "C::F(object)"], Calls(procedure).Select(static c => c.Callee.Value), StringComparer.Ordinal);
    }

    /// <summary>An object that is nested in another one is not an argument, and the outer one takes its opaque value.</summary>
    [Fact]
    public void AnObjectNestedInAnArgumentStaysOpaque()
    {
        IrProcedure procedure = Method(F + "static void M(int x) { F(new { Inner = new { X = x } }); }");

        IrOpaque inner = Assert.Single(Opaques(procedure));
        Assert.Equal("AnonymousObjectCreation", inner.Reason);
        Assert.Equal(inner.Target, Calls(procedure)[0].Args[0]);
    }

    /// <summary>Criterion 2 lowers only the argument: an object returned, kept in a local or used as a receiver stays opaque.</summary>
    [Theory]
    [InlineData("static object M(int x) { return new { X = x }; }")]
    [InlineData("static object M(int x) => new { X = x };")]
    [InlineData("static void M(int x) { var p = new { X = x }; F(p); }")]
    [InlineData("static string M(int x) => new { X = x }.ToString();")]
    [InlineData("static void M(int x) { F(new[] { new { X = x } }); }")]
    [InlineData("static void M(int x, bool b) { F(b ? new { X = x } : null); }")]
    public void AnObjectThatIsNotAnArgumentStaysOpaque(string members)
    {
        IrProcedure procedure = Method(F + members);

        Assert.Contains(Opaques(procedure), static o => o.Reason is "AnonymousObjectCreation");
        Assert.DoesNotContain(Calls(procedure), static c => c.Callee.Value.StartsWith('{'));
    }
}
