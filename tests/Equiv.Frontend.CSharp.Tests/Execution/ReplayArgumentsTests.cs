using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>Ticket M4-009: a model's inputs bound to each side and turned into the driver's wire arguments.</summary>
public sealed class ReplayArgumentsTests
{
    private const string Source = """
        namespace N
        {
            public enum E : short { A = -1 }
            public enum U : uint { A }

            public class C
            {
                public static int All(bool b, char c, sbyte s8, byte u8, short s16, ushort u16, int s32, uint u32, long s64, ulong u64, float f, double d, decimal m, string s, string t, object o, E e, U u) => 0;
                public int Instance(int x) => x;
                internal static int Hidden() => 0;
                public int Settable { set { } }
                public int Size => 2;
                public static int Flag(bool b) => 0;
                public static T Generic<T>(T x) => x;
                public static int ByRef(ref int x) => x;
                public static int When(System.DateTime d) => 0;
            }

            public class G<T> { public static int M() => 0; }
            internal class Inner { public static int M() => 0; }
            public abstract class A { public int M() => 0; }
            public class P { private P() { } public int M() => 0; }
            public class Q { public Q(int x) { } public int M() => 0; }
            public struct S { public int M() => 0; }
        }
        """;

    private const string Empty = "proc \"X\" () entry B0 B0: ret";

    private static readonly Compilation Project = Compile(Source);

    private static readonly Dictionary<string, IrValue> NoValues = new(StringComparer.Ordinal);

    private static IrBitVecValue Bv(int width, long value) => IrBitVecValue.FromSigned(width, value);

    private static ReplayArguments.SideCase Case(string type, string method, string ir, Dictionary<string, IrValue> values) =>
        ReplayArguments.Case(Method(Project, type, method), IrText.Parse(ir), values, ReplayArguments.Nullness(values));

    [Fact]
    public void EveryBuildableTypeBecomesItsWireValue()
    {
        const string ir = """
            proc "X" (%b: bool, %c: bv16, %s8: bv8, %u8: bv8, %s16: bv16, %u16: bv16, %s32: bv32, %u32: bv32, %s64: bv64, %u64: bv64,
                %f: sort "System.Single", %d: sort "System.Double", %m: sort "System.Decimal", %s: sort "System.String", %t: sort "System.String",
                %o: sort "System.Object", %e: bv16, %u: bv32, %null.System.String: map<sort "System.String", bool>, %null.System.Object: map<sort "System.Object", bool>)
                entry B0 B0: ret
            """;
        Dictionary<string, IrValue> values = new(StringComparer.Ordinal)
        {
            ["b"] = new IrBoolValue(Value: true),
            ["c"] = Bv(16, 'A'),
            ["s8"] = Bv(8, -1),
            ["u8"] = Bv(8, 255),
            ["s16"] = Bv(16, -2),
            ["u16"] = Bv(16, 65535),
            ["s32"] = Bv(32, -3),
            ["u32"] = Bv(32, uint.MaxValue),
            ["s64"] = Bv(64, -4),
            ["u64"] = new IrBitVecValue(64, ulong.MaxValue),
            ["f"] = new IrSortValue("System.Single", 1),
            ["d"] = new IrSortValue("System.Double", 2),
            ["m"] = new IrSortValue("System.Decimal", 3),
            ["s"] = new IrSortValue("System.String", 7),
            ["t"] = new IrSortValue("System.String", 8),
            ["o"] = new IrSortValue("System.Object", 9),
            ["e"] = Bv(16, -1),
            ["u"] = Bv(32, uint.MaxValue),
            ["null.System.String"] = Nulls("System.String", 8),
            ["null.System.Object"] = Nulls("System.Object", 9),
        };

        ReplayArguments.SideCase built = Case("N.C", "All", ir, values);

        Assert.Equal(
            [
                "true", "65", "-1", "255", "-2", "65535", "-3", "4294967295", "-4", "18446744073709551615",
                "\"0x3F800000\"", "\"0x4000000000000000\"", "[3,0,0,0]", "\"s7\"", "null", "null", "-1", "4294967295",
            ],
            built.Input!.Arguments,
            StringComparer.Ordinal);
        Assert.Empty(built.Reason);
    }

    [Theory]
    [InlineData("N.C", "Hidden", Empty, "not public")]
    [InlineData("N.Inner", "M", Empty, "not public")]
    [InlineData("N.C", "set_Settable", "proc \"X\" (%value: bv32, %this: sort \"N.C\") entry B0 B0: ret", "not a method or a property getter")]
    [InlineData("N.C", "Generic", "proc \"X\" (%x: sort \"T\") entry B0 B0: ret", "generic")]
    [InlineData("N.G`1", "M", Empty, "generic")]
    [InlineData("N.C", "ByRef", "proc \"X\" (ref %x: bv32) entry B0 B0: ret", "x is passed by reference")]
    [InlineData("N.A", "M", "proc \"X\" (%this: sort \"N.A\") entry B0 B0: ret", "N.A has no public parameterless constructor")]
    [InlineData("N.P", "M", "proc \"X\" (%this: sort \"N.P\") entry B0 B0: ret", "N.P has no public parameterless constructor")]
    [InlineData("N.Q", "M", "proc \"X\" (%this: sort \"N.Q\") entry B0 B0: ret", "N.Q has no public parameterless constructor")]
    [InlineData(
        "N.C",
        "Instance",
        "proc \"X\" (%x: bv32, %field.N.C.f: map<sort \"N.C\", bv32>, %cast.N.C.System.Object: map<sort \"N.C\", sort \"System.Object\">, %this: sort \"N.C\") entry B0 B0: ret",
        "the model constrains field.N.C.f, cast.N.C.System.Object")]
    public void AMethodGeneratedSourceCannotCallWithTheModelIsNotConstructible(string type, string method, string ir, string reason)
    {
        ReplayArguments.SideCase built = Case(type, method, ir, NoValues);

        Assert.Null(built.Input);
        Assert.Equal(reason, built.Reason);
    }

    [Fact]
    public void AGetterAndFalseAreBuilt()
    {
        Assert.Empty(Case("N.C", "get_Size", "proc \"X\" (%this: sort \"N.C\") entry B0 B0: ret", NoValues).Input!.Arguments);
        Dictionary<string, IrValue> values = new(StringComparer.Ordinal) { ["b"] = new IrBoolValue(Value: false) };
        Assert.Equal(["false"], Case("N.C", "Flag", "proc \"X\" (%b: bool) entry B0 B0: ret", values).Input!.Arguments, StringComparer.Ordinal);
    }

    [Fact]
    public void AStructReceiverIsItsDefault() =>
        Assert.Empty(Case("N.S", "M", "proc \"X\" (%this: sort \"N.S\") entry B0 B0: ret", NoValues).Input!.Arguments);

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "the model's receiver is null")]
    public void AReceiverIsBuiltUnlessTheModelMakesItNull(bool isNull, string? reason)
    {
        Dictionary<string, IrValue> values = new(StringComparer.Ordinal)
        {
            ["x"] = Bv(32, 1),
            ["this"] = new IrSortValue("N.C", 4),
            ["null.N.C"] = isNull ? Nulls("N.C", 4) : Nulls("N.C"),
        };

        ReplayArguments.SideCase built = Case("N.C", "Instance", "proc \"X\" (%x: bv32, %this: sort \"N.C\", %null.N.C: map<sort \"N.C\", bool>) entry B0 B0: ret", values);

        Assert.Equal(reason ?? string.Empty, built.Reason);
        Assert.Equal(reason is null, built.Input is not null);
    }

    /// <summary>A value of the wrong kind for the C# parameter, or a type the generators never build, has no argument.</summary>
    [Theory]
    [InlineData("N.C", "Instance", "x", "int")]
    [InlineData("N.C", "When", "d", "System.DateTime")]
    public void AValueWithNoArgumentIsNotConstructible(string type, string method, string parameter, string display)
    {
        (IrValue Value, string Type)[] candidates =
        [
            (new IrBoolValue(Value: true), "bool"), (Bv(32, 1), "bv32"), (new IrSortValue("S", 1), "sort \"S\""), (Nulls("S"), "map<sort \"S\", bool>"),
        ];
        foreach ((IrValue value, string irType) in candidates)
        {
            string ir = $"proc \"X\" (%{parameter}: {irType}) entry B0 B0: ret";
            Dictionary<string, IrValue> values = new(StringComparer.Ordinal) { [parameter] = value };

            ReplayArguments.SideCase built = ReplayArguments.Case(Method(Project, type, method), IrText.Parse(ir), values, ReplayArguments.Nullness(values));

            if (built.Input is null)
            {
                Assert.Equal($"no {display} argument can be built for {parameter} from the model", built.Reason);
            }
            else
            {
                // Only a bitvector builds an int.
                Assert.Equal(("int", "1"), (display, Assert.Single(built.Input.Arguments)));
            }
        }
    }

    [Fact]
    public void AStringIsNullOnlyWhenTheNullMapSaysTrue()
    {
        IrMapValue widths = new(new IrMap(new IrSort("System.String"), new IrBitVec(32)), Bv(32, 1), []);
        Assert.Equal(["\"s5\"", "null"], Strings(widths, 5).Concat(Strings(Nulls("System.String", 5), 5)), StringComparer.Ordinal);
        Assert.Equal(["\"s5\""], Strings(Nulls("System.String", 6), 5), StringComparer.Ordinal);
        Assert.Equal(["\"s5\""], Strings(nullMap: null, 5), StringComparer.Ordinal);

        static IEnumerable<string> Strings(IrMapValue? nullMap, int id)
        {
            Dictionary<string, IrValue> values = new(StringComparer.Ordinal) { ["s"] = new IrSortValue("System.String", id) };
            if (nullMap is not null)
            {
                values["null.System.String"] = nullMap;
            }

            ReplayArguments.SideCase built = ReplayArguments.Case(
                ReplayCompilations.Method(Compile("public static class K { public static int M(string s) => 0; }"), "K", "M"),
                IrText.Parse("proc \"X\" (%s: sort \"System.String\") entry B0 B0: ret"),
                values,
                ReplayArguments.Nullness(values));
            return built.Input!.Arguments;
        }
    }

    [Fact]
    public void Bind_PairsCSharpParametersByPositionAndSynthesisedOnesByName()
    {
        IrProcedure old = IrText.Parse("proc \"X\" (%a: bv32, %w: bv32, %null.S: map<sort \"S\", bool>) entry B0 B0: ret");
        IrProcedure @new = IrText.Parse("proc \"X\" (%b: bv32, %w: bv64, %c: bool, %null.S: map<sort \"S\", bool>) entry B0 B0: ret");
        IrValue a = Bv(32, 1), w = Bv(32, 2), nulls = Nulls("S"), newW = Bv(64, 3), c = new IrBoolValue(Value: true);

        (Dictionary<string, IrValue> oldValues, Dictionary<string, IrValue> newValues) = ReplayArguments.Bind(old, @new, new IrInputs([a, w, nulls, newW, c]))!.Value;

        Assert.Equal([("a", a), ("w", w), ("null.S", nulls)], oldValues.Select(static p => (p.Key, p.Value)));
        Assert.Equal([("b", a), ("null.S", nulls), ("w", newW), ("c", c)], newValues.Select(static p => (p.Key, p.Value)));
    }

    [Fact]
    public void Bind_AParameterOnlyTheOldSideHasBindsTheOldSideOnly()
    {
        IrProcedure old = IrText.Parse("proc \"X\" (%a: bv32, %null.S: map<sort \"S\", bool>) entry B0 B0: ret");
        IrProcedure @new = IrText.Parse("proc \"X\" () entry B0 B0: ret");

        (Dictionary<string, IrValue> oldValues, Dictionary<string, IrValue> newValues) = ReplayArguments.Bind(old, @new, new IrInputs([Bv(32, 1), Nulls("S")]))!.Value;

        Assert.Equal(["a", "null.S"], oldValues.Keys, StringComparer.Ordinal);
        Assert.Empty(newValues);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Bind_AModelOverOtherParametersIsNull(int count)
    {
        IrProcedure old = IrText.Parse("proc \"X\" (%a: bv32) entry B0 B0: ret");
        IrProcedure @new = IrText.Parse("proc \"X\" (%a: bv64) entry B0 B0: ret");
        IrValue[] values = [Bv(32, 1), Bv(64, 1), Bv(64, 1)];

        Assert.Null(ReplayArguments.Bind(old, @new, new IrInputs([.. values.Take(count)])));
        Assert.Null(ReplayArguments.Bind(old, @new, new IrInputs([Bv(64, 1), Bv(64, 1)])));
    }
}
