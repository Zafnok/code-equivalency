using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// A <c>Nullable&lt;T&gt;</c> is a value of its sort and a null shadow that says it has no value (ticket P2-095). For a
/// <c>bool</c> or integral <c>T</c>, <c>T</c> converted to <c>T?</c> is a read of <c>cast.&lt;T&gt;.System.Nullable_1</c> at
/// the value, as boxing is, and is never null; <c>null</c> and <c>default(T?)</c> are the null constant of the sort, and
/// always null; and the CFG's null test reads the shadow.
/// </summary>
public sealed class NullableLoweringTests
{
    private static readonly IrSort Nullable = new("System.Nullable`1");

    [Theory]
    [InlineData("static int? M(int a) => a;", "cast.System.Int32.System.Nullable_1")]
    [InlineData("static int? M(int a) => (int?)a;", "cast.System.Int32.System.Nullable_1")]
    [InlineData("static int? M(int a) => new int?(a);", "cast.System.Int32.System.Nullable_1")]
    [InlineData("static long? M(long a) => a;", "cast.System.Int64.System.Nullable_1")]
    [InlineData("static char? M(char a) => a;", "cast.System.Char.System.Nullable_1")]
    [InlineData("static bool? M(bool a) => a;", "cast.System.Boolean.System.Nullable_1")]
    public void AConversionToNullableIsAReadOfItsCastMap(string members, string map)
    {
        IrProcedure procedure = Method(members);

        IrMapRead read = Assert.Single(Instructions(procedure).OfType<IrMapRead>());
        Assert.Equal(map, read.Map.Name);
        Assert.Equal(procedure.Parameters[0].Var, read.Key);
        Assert.Equal(Nullable, read.Target.Type);
        Assert.Equal(IrParameterKind.In, Assert.Single(procedure.Parameters, p => p.Var == read.Map).Kind);
        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
    }

    /// <summary>Acceptance criterion 2: however "no value" is spelled, it is the one null constant of the sort.</summary>
    [Theory]
    [InlineData("static int? M() => null;")]
    [InlineData("static int? M() => (int?)null;")]
    [InlineData("static int? M() => default;")]
    [InlineData("static int? M() => default(int?);")]
    [InlineData("static int? M() => new int?();")]
    [InlineData("static bool? M() => default(bool?);")]
    [InlineData("static int? M(bool b) { int? r; if (b) { r = null; } else { r = default; } return r; }")]
    [InlineData("static int? M(bool b) => b ? null : default(int?);")]
    public void NullAndDefaultAreTheOneNullConstant(string members)
    {
        IrProcedure procedure = Method(members);
        ImmutableArray<IrConst> values = [.. Instructions(procedure).OfType<IrConst>().Where(static c => c.Target.Type is IrSort)];

        Assert.NotEmpty(values);
        Assert.All(values, static c => Assert.Equal(new IrSortValue("System.Nullable`1", 0), c.Value));
        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
    }

    /// <summary>
    /// Acceptance criterion 3: the CFG's null test of a <c>Nullable&lt;T&gt;</c> reads its shadow, which a conversion from
    /// <c>T</c> sets to false and <c>null</c>, <c>default</c> and <c>new T?()</c> to true, so <c>??</c> branches on a constant.
    /// </summary>
    [Theory]
    [InlineData("static int M(int a, int b) { int? t = a; return t ?? b; }", false)]
    [InlineData("static int M(int a, int b) { int? t = new int?(a); return t ?? b; }", false)]
    [InlineData("static int M(int a, int b) { int? t = null; return t ?? b; }", true)]
    [InlineData("static int M(int a, int b) { int? t = default; return t ?? b; }", true)]
    [InlineData("static int M(int a, int b) { int? t = default(int?); return t ?? b; }", true)]
    [InlineData("static int M(int a, int b) { int? t = new int?(); return t ?? b; }", true)]
    [InlineData("static double M(double a, double b) { double? t = a; return t ?? b; }", false)]
    [InlineData("static double M(double a, double b) { double? t = default(double?); return t ?? b; }", true)]
    public void TheNullTestReadsTheShadowAConversionOrANullSet(string members, bool isNull)
    {
        IrProcedure procedure = Method(members);
        ImmutableHashSet<IrVar> threw = Threw(procedure);

        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name is "null.System.Nullable_1");
        IrBranch test = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrBranch>(), b => !threw.Contains(b.Cond));
        Assert.Equal(new IrBoolValue(isNull), Assert.Single(Instructions(procedure).OfType<IrConst>(), c => c.Target == test.Cond).Value);
        Assert.DoesNotContain(Opaques(procedure), static o => o.Reason is "IsNull");
    }

    /// <summary>
    /// A user-defined conversion to a <c>Nullable&lt;T&gt;</c> may return <c>null</c>, and an unboxing yields it for a null
    /// reference, so neither sets the shadow to false: the first asks <c>null.System.Nullable_1</c>, the second follows the reference.
    /// </summary>
    [Theory]
    [InlineData("struct S { public static implicit operator int?(S s) => null; } static int M(S s, int b) { int? t = s; return t ?? b; }", "null.System.Nullable_1")]
    [InlineData("static int M(object o, int b) { int? t = (int?)o; return t ?? b; }", "null.System.Object")]
    public void AConversionThatMayYieldNullAsksItsNullMap(string members, string map)
    {
        IrProcedure procedure = Method(members);

        IrMapRead read = Assert.Single(Instructions(procedure).OfType<IrMapRead>(), r => string.Equals(r.Map.Name, map, StringComparison.Ordinal));
        Assert.Contains(procedure.Blocks, b => b.Terminator is IrBranch branch && branch.Cond == read.Target);
    }

    /// <summary>A <c>Nullable&lt;T&gt;</c> parameter's shadow starts as its <c>null.System.Nullable_1</c> read, as a reference's does.</summary>
    [Theory]
    [InlineData("static int M(int? n) => n ?? 0;")]
    [InlineData("static double M(double? n) => n ?? 0;")]
    public void AParameterStartsWithItsNullMapRead(string members)
    {
        IrProcedure procedure = Method(members);

        IrMapRead read = Assert.Single(Instructions(procedure).OfType<IrMapRead>());
        Assert.Equal("null.System.Nullable_1", read.Map.Name);
        Assert.Equal(procedure.Parameters[0].Var, read.Key);
        Assert.Contains(procedure.Blocks, b => b.Terminator is IrBranch branch && branch.Cond == read.Target);
    }

    /// <summary>The shadow joins as the value does: <c>s?.Length</c> has no value exactly when <c>s</c> is null.</summary>
    [Fact]
    public void TheShadowOfANullConditionalFollowsItsReceiver()
    {
        IrProcedure procedure = Method("static int M(string s) => s?.Length ?? 0;");

        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name is "null.System.Nullable_1");
        Assert.Contains(Instructions(procedure).OfType<IrPhi>(), static p => p.Target.Type is IrBool && p.Target.Name.Contains(".isNull", StringComparison.Ordinal));
        Assert.DoesNotContain(Opaques(procedure), static o => o.Reason is "IsNull" or "Conversion" or "DefaultValue");
    }

    /// <summary>
    /// The conversion around a target-typed conditional, whose arms are already of the target type, is its operand: the
    /// value and the shadow the arms joined.
    /// </summary>
    [Theory]
    [InlineData("static int? M(int a, bool b) => b ? a : null;")]
    [InlineData("static int? M(int a, bool b) => b ? null : a;")]
    [InlineData("static long M(int a, bool b) { int? t = b ? a : null; return t ?? 1L; }")]
    public void ATargetTypedConditionalIsItsOperand(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Contains(Instructions(procedure).OfType<IrPhi>(), static p => p.Target.Type == Nullable);
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name is "null.System.Nullable_1");
    }

    /// <summary>Out of scope: a conversion that also converts the value, one from a <c>Nullable&lt;T&gt;</c>, and one of any other <c>T</c>.</summary>
    [Theory]
    [InlineData("static int? M(short s) => s;", "Conversion")]
    [InlineData("static long? M(int? n) => n;", "Conversion")]
    [InlineData("static int M(int? n) => (int)n;", "Conversion")]
    [InlineData("static double? M(double d) => d;", "Conversion")]
    [InlineData("enum E { A } static E? M(E e) => e;", "Conversion")]
    [InlineData("static double? M() => default(double?);", "DefaultValue")]
    [InlineData("struct S { } static S? M() => default(S?);", "DefaultValue")]
    public void AnyOtherNullableConversionOrDefaultStaysOpaque(string members, string reason) =>
        Assert.Contains(Opaques(Method(members)), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));

    /// <summary>A <c>Nullable&lt;T&gt;</c> of any other <c>T</c> is still created by a call to its constructor.</summary>
    [Theory]
    [InlineData("static double? M(double d) => new double?(d);")]
    [InlineData("static double? M() => new double?();")]
    public void ANullableOfAnotherTypeIsStillConstructed(string members) =>
        Assert.Single(Calls(Method(members)));

    private static ImmutableArray<IrInstruction> Instructions(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions)];

    /// <summary>The <c>threw</c> flags of the procedure's calls and fragments, whose branches are no null test.</summary>
    private static ImmutableHashSet<IrVar> Threw(IrProcedure procedure) =>
        [.. Instructions(procedure).Select(static i => i switch { IrCall call => call.Threw, IrOpaque opaque => opaque.Threw, _ => null }).OfType<IrVar>()];
}
