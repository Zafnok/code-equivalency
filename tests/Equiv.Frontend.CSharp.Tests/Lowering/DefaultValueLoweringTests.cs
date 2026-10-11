using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// The <c>default</c> the compiler passes for an optional parameter of a struct type is element 0 of the type's sort
/// (ticket P2-138): one value per type, so two of them are equal and both sides of a pair pass the same one. Any other
/// <c>default</c> that is no compile-time constant stays opaque with reason <c>DefaultValue</c>.
/// </summary>
public sealed class DefaultValueLoweringTests
{
    private const string Stamp = "struct Stamp { public int N; }\n";

    /// <summary>Acceptance criterion 3, a user-defined struct: the omitted argument is the constant, and nothing is opaque.</summary>
    [Theory]
    [InlineData("static int F(int k, Stamp s = default) => k; static int M(int a) => F(a);", "C+Stamp")]
    [InlineData("static int F(int k, Stamp s = new Stamp()) => k; static int M(int a) => F(a);", "C+Stamp")]
    [InlineData("static int F(int k, Stamp s = default, int n = 3) => k; static int M(int a) => F(a, n: 4);", "C+Stamp")]
    [InlineData("C(Stamp s = default) { } static C M() => new C();", "C+Stamp")]
    [InlineData("struct Box<T> { public T Value; } static int F<T>(Box<T> b = default) => 0; static int M() => F<string>();", "C+Box`1")]
    public void TheDefaultPassedForAnOptionalUserStructParameterIsElementZeroOfItsSort(string members, string sort) =>
        AssertPassesElementZero(Method(Stamp + members), sort);

    /// <summary>Acceptance criterion 3, a base class library struct: <c>CancellationToken</c> and any other one alike.</summary>
    [Theory]
    [InlineData("static int F(int k, System.Threading.CancellationToken t = default) => k; static int M(int a) => F(a);", "System.Threading.CancellationToken")]
    [InlineData("static System.Threading.Tasks.Task M() => System.Threading.Tasks.Task.Delay(1);", null)]
    [InlineData("static System.Threading.Tasks.Task<string> M(string p) => System.IO.File.ReadAllTextAsync(p);", "System.Threading.CancellationToken")]
    [InlineData("static int F(DateTime d = default) => 0; static int M() => F();", "System.DateTime")]
    [InlineData("static int F(Guid g = default) => 0; static int M() => F();", "System.Guid")]
    [InlineData("static int F((string, int) t = default) => 0; static int M() => F();", "System.ValueTuple`2")]
    public void TheDefaultPassedForAnOptionalLibraryStructParameterIsElementZeroOfItsSort(string members, string? sort)
    {
        IrProcedure procedure = Method(members);

        if (sort is null)
        {
            // `Task.Delay(int)` has no optional parameter: nothing is passed, so nothing is made.
            Assert.DoesNotContain(Instructions(procedure), static i => i is IrConst { Value: IrSortValue });
            Assert.Empty(Opaques(procedure));
            return;
        }

        AssertPassesElementZero(procedure, sort);
    }

    /// <summary>Acceptance criterion 2: two defaults of one type are one value, and defaults of two types are of two sorts.</summary>
    [Fact]
    public void TwoDefaultsOfOneTypeAreTheSameValue()
    {
        IrProcedure procedure = Method(
            Stamp + "static int F(Stamp s = default) => 0; static int G(int k, Stamp s = default, Guid g = default) => k; static int M(int a) => F() + G(a);");

        ImmutableArray<IrCall> calls = Calls(procedure);
        Assert.Equal(2, calls.Length);
        IrSortValue[] passed = [.. calls.SelectMany(static c => c.Args).Select(arg => Constant(procedure, arg)).OfType<IrSortValue>()];
        Assert.Equal([new IrSortValue("C+Stamp", 0), new IrSortValue("C+Stamp", 0), new IrSortValue("System.Guid", 0)], passed);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>
    /// What the ticket leaves opaque: a <c>default</c> a person wrote, which can be stored and have its fields read; a
    /// <c>Nullable&lt;T&gt;</c> ticket P2-095 left out; a type parameter's; and a tuple of an <see cref="IrTuple"/> sort, whose
    /// value is its elements.
    /// </summary>
    [Theory]
    [InlineData("static int F(Stamp s = default) => 0; static int M() => F(default);", "Conversion")]
    [InlineData("static int F(Stamp s = default) => 0; static int M() => F(default(Stamp));", "DefaultValue")]
    [InlineData("static Stamp M() => default(Stamp);", "DefaultValue")]
    [InlineData("static int M() { Stamp s = default(Stamp); return s.N; }", "DefaultValue")]
    [InlineData("static int F(DayOfWeek? d = null) => 0; static int M() => F();", "DefaultValue")]
    [InlineData("static int F(Stamp? s = null) => 0; static int M() => F();", "DefaultValue")]
    [InlineData("static int F<T>(T t = default) => 0; static int M<T>() => F<T>();", "DefaultValue")]
    [InlineData("static int F<T>(T t = default) where T : struct => 0; static int M<T>() where T : struct => F<T>();", "DefaultValue")]
    [InlineData("static int F((int, bool) t = default) => 0; static int M() => F();", "DefaultValue")]
    public void AnyOtherDefaultThatIsNoConstantStaysOpaque(string members, string reason)
    {
        IrProcedure procedure = Method(Stamp + members);

        Assert.Equal(reason, Assert.Single(Opaques(procedure)).Reason);
    }

    /// <summary>
    /// An optional parameter whose default is a compile-time constant is that constant, as it always was: never element 0,
    /// which in a numeric or enum sort is no number.
    /// </summary>
    [Theory]
    [InlineData("static int F(int k = default) => k; static int M() => F();", 0)]
    [InlineData("static int F(int k = 5) => k; static int M() => F();", 5)]
    [InlineData("static int F(DayOfWeek d = default) => 0; static int M() => F();", null)]
    [InlineData("static int F(decimal d = default) => 0; static int M() => F();", null)]
    [InlineData("static int F(nint p = default) => 0; static int M() => F();", null)]
    [InlineData("static int F(bool b = default) => 0; static int M() => F();", null)]
    public void AConstantDefaultIsStillItsConstant(string members, int? value)
    {
        IrProcedure procedure = Method(members);

        IrValue? passed = Constant(procedure, Assert.Single(Assert.Single(Calls(procedure)).Args));
        Assert.NotNull(passed);
        Assert.False(passed is IrSortValue { Id: 0 });
        Assert.True(value is null || passed == Bits(32, value.Value));
        Assert.Empty(Opaques(procedure));
    }

    private static void AssertPassesElementZero(IrProcedure procedure, string sort)
    {
        IrCall call = Assert.Single(Calls(procedure));
        IrSortValue[] passed = [.. call.Args.Select(arg => Constant(procedure, arg)).OfType<IrSortValue>()];

        Assert.Equal([new IrSortValue(sort, 0)], passed);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>The constant <paramref name="variable"/> is defined as, or null when an instruction of another kind defines it.</summary>
    private static IrValue? Constant(IrProcedure procedure, IrVar variable) =>
        Instructions(procedure).OfType<IrConst>().SingleOrDefault(c => c.Target == variable)?.Value;

    private static IEnumerable<IrInstruction> Instructions(IrProcedure procedure) => procedure.Blocks.SelectMany(static b => b.Instructions);
}
