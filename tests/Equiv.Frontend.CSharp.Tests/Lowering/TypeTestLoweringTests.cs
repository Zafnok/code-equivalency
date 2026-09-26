using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket M4-005: <c>is</c>, <c>as</c>, downcasts and type patterns read the <c>istype.&lt;From&gt;.&lt;To&gt;</c> predicate
/// at a non-null operand, and the converted value is a read of M3-010's <c>cast.&lt;From&gt;.&lt;To&gt;</c> map.
/// </summary>
public sealed class TypeTestLoweringTests
{
    private const string Object = "System.Object";
    private const string String = "System.String";

    [Theory]
    [InlineData("static bool M(object o) => o is string;", "istype.System.Object.System.String")]
    [InlineData("static bool M(object o) => o is string _;", "istype.System.Object.System.String")]
    public void IsTypeReadsTheTypeMap(string members, string name)
    {
        IrProcedure procedure = Method(members);

        IrParameter isType = Assert.Single(procedure.Parameters, p => string.Equals(p.Var.Name, name, StringComparison.Ordinal));
        Assert.Equal(IrParameterKind.In, isType.Kind);
        Assert.Equal(new IrMap(new IrSort(Object), new IrBool()), isType.Var.Type);
        Assert.Empty(Calls(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), Run(procedure, Reference(1, Object), Always(Object, value: true), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), Run(procedure, Reference(1, Object), Always(Object, value: false), Nulls(Object, 1, isNull: false)));
    }

    [Fact]
    public void IsNullIsFalse() =>
        Assert.Equal(
            new IrReturned(new IrBoolValue(Value: false)),
            Run(Method("static bool M(object o) => o is string;"), Reference(1, Object), Always(Object, value: true), Nulls(Object, 1, isNull: true)));

    /// <summary>An identity test and a test of <c>this</c>, which is never null, read the predicate too.</summary>
    [Theory]
    [InlineData("static bool M(string s) => s is string;", "istype.System.String.System.String")]
    [InlineData("bool M() => this is IComparable;", "istype.C.System.IComparable")]
    public void EveryModelledTestReadsThePredicate(string members, string name)
    {
        IrProcedure procedure = Method(members);

        Assert.Contains(procedure.Parameters, p => string.Equals(p.Var.Name, name, StringComparison.Ordinal));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void DeclarationPatternBindsThroughTheCastMap()
    {
        IrProcedure procedure = Method("static string M(object o) => o is string s && s != null ? s : null;");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(
            ["o", "cast.System.Object.System.String", "istype.System.Object.System.String", "null.System.Object"],
            procedure.Parameters.Select(static p => p.Var.Name),
            StringComparer.Ordinal);
        IrMapValue cast = Cast(1, Reference(9));
        Assert.Equal(new IrReturned(Reference(9)), Run(procedure, Reference(1, Object), cast, Always(Object, value: true), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(Reference(0)), Run(procedure, Reference(1, Object), cast, Always(Object, value: false), Nulls(Object, 1, isNull: false)));
    }

    [Fact]
    public void AsYieldsNullWhenTheTestFails()
    {
        IrProcedure procedure = Method("static string M(object o) => o as string;");
        IrProcedure nullness = Method("static bool M(object o) { string s = o as string; return s == null; }");

        IrMapValue cast = Cast(1, Reference(9));
        Assert.Equal(new IrReturned(Reference(9)), Run(procedure, Reference(1, Object), cast, Always(Object, value: true), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(Reference(0)), Run(procedure, Reference(1, Object), cast, Always(Object, value: false), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(Reference(0)), Run(procedure, Reference(1, Object), cast, Always(Object, value: true), Nulls(Object, 1, isNull: true)));
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), Run(nullness, Reference(1, Object), cast, Always(Object, value: false), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), Run(nullness, Reference(1, Object), cast, Always(Object, value: true), Nulls(Object, 1, isNull: false)));
        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
    }

    [Fact]
    public void FailingDowncastThrowsInvalidCast()
    {
        IrProcedure procedure = Method("static string M(object o) => (string)o;");

        IrMapValue cast = Cast(1, Reference(9));
        Assert.Equal(new IrThrew("System.InvalidCastException"), Run(procedure, Reference(1, Object), cast, Always(Object, value: false), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(Reference(9)), Run(procedure, Reference(1, Object), cast, Always(Object, value: true), Nulls(Object, 1, isNull: false)));
        Assert.Equal(new IrReturned(Reference(0)), Run(procedure, Reference(1, Object), cast, Always(Object, value: false), Nulls(Object, 1, isNull: true)));
        Assert.Empty(Opaques(procedure));
    }

    /// <summary><c>this</c> is never null, so its downcast has no null branch: it throws or converts.</summary>
    [Fact]
    public void DowncastOfThisIsTheCastOrAThrow()
    {
        IrProcedure procedure = Method("class D : C { } D M() => (D)this;");

        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("null.", StringComparison.Ordinal));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name.StartsWith("istype.C.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("static int M(object o) => (int)o;", "Conversion")]
    [InlineData("static int? M(object o) => o as int?;", "Conversion")]
    [InlineData("static bool M(object o) => o is int;", "IsType")]
    [InlineData("static bool M(int i) => i is IComparable;", "IsType")]
    [InlineData("static int M(object o) => o is int n ? n : 0;", "switch-pattern")]
    [InlineData("static bool M(object o) => o is var v && v != null;", "switch-pattern")]
    [InlineData("sealed class A { } sealed class B { } static bool M(A a) => a is B;", "IsType")]
    [InlineData("class A { public static explicit operator B(A a) => null; } class B { } static bool M(A a) => a is B;", "IsType")]
    [InlineData("static bool M<T>(object o) => o is T;", "IsType")]
    [InlineData("static bool M<T>(T t) where T : class => t is string;", "IsType")]
    [InlineData("static T M<T>(object o) where T : class => (T)o;", "Conversion")]
    [InlineData("static T M<T>(object o) where T : class => o as T;", "Conversion")]
    public void UnboxingStaysOpaque(string members, string reason)
    {
        IrProcedure procedure = Method(members);

        Assert.Contains(Opaques(procedure), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("istype.", StringComparison.Ordinal));
    }

    /// <summary>A predicate over <paramref name="sort"/> that answers <paramref name="value"/> everywhere.</summary>
    private static IrMapValue Always(string sort, bool value) => new(new IrMap(new IrSort(sort), new IrBool()), new IrBoolValue(value), []);

    /// <summary>A <c>cast.System.Object.System.String</c> input that maps object <paramref name="id"/> to <paramref name="result"/>.</summary>
    private static IrMapValue Cast(int id, IrSortValue result) => new(
        new IrMap(new IrSort(Object), new IrSort(String)),
        Reference(7),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(Reference(id, Object), result));
}
