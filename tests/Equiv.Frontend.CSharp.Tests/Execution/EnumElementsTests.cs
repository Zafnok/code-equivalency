using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Execution;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-251 criterion 1: a replay builds an enum argument or field from the sort element the model holds it as. A
/// member's constant element is that member, and any other element a value no member has, distinct per element.
/// </summary>
public sealed class EnumElementsTests
{
    private const string Source = """
        namespace N
        {
            public enum Color { Red = 1, Green = 2, Blue = 7 }
            [System.Flags] public enum Bits : byte { A = 1, B = 2, C = 16 }
            [System.Flags] public enum Signed : sbyte { A = 1, Rest = 126 }
            public enum Full : byte { Low = 0, High = 255 }
            public enum Negative : short { M = -5, Z = -1 }
            public enum Shrink { One = 1 }
            public class Holder { internal Color Paint; }

            public static class K
            {
                public static int Colors(Color a, Color b, Color c, Color d, Color e) => 0;
                public static int Flagged(Bits a, Bits b, Bits c) => 0;
                public static int Top(Signed s) => 0;
                public static int Edge(Full f) => 0;
                public static int Below(Negative n, Negative m) => 0;
                public static int Framework(System.StringComparison s, System.StringComparison t) => 0;
                public static int Held(Holder h) => 0;
                public static int Grown(Shrink s, Shrink t) => 0;
            }
        }
        """;

    /// <summary>The other side: <c>Shrink</c> has a member this side lacks, and <c>Color</c> is no enum.</summary>
    private const string Other = "namespace N { public enum Shrink { One = 1, Two = 5 } public class Color { public const int Red = 99; } }";

    private static readonly Compilation Project = Compile(Source);

    private static readonly Compilation Counterpart = Compile(Other, "Other");

    /// <summary>The element the lowering gives the constant <paramref name="value"/> of <paramref name="type"/> in <paramref name="compilation"/>.</summary>
    private static IrSortValue Constant(string type, object value, Compilation? compilation = null) =>
        (IrSortValue)TypeMapper.Constant((compilation ?? Project).GetTypeByMetadataName(type)!, value);

    private static ReplayArguments.SideCase Case(string method, IrValue[] arguments, Compilation? counterpart = null, Dictionary<string, IrValue>? heap = null)
    {
        IMethodSymbol symbol = Method(Project, "N.K", method);
        Dictionary<string, IrValue> values = symbol.Parameters.Zip(arguments).ToDictionary(static p => p.First.Name, static p => p.Second, StringComparer.Ordinal);
        foreach ((string name, IrValue value) in heap ?? [])
        {
            values[name] = value;
        }

        string parameters = string.Join(", ", symbol.Parameters.Select(static p => $"%{p.Name}: bool"));
        IrValue[] inputs = [.. values.Values, IrBitVecValue.FromSigned(32, 3)];
        return ReplayArguments.Case(symbol, Project, IrText.Parse($"proc \"X\" ({parameters}) entry B0 B0: ret"), values, ReplayArguments.Heap(values), Counterexample(inputs), counterpart);
    }

    private static string[] Wires(ReplayArguments.SideCase built)
    {
        Assert.True(built.Input is not null, built.Reason);
        return [.. built.Input.Arguments];
    }

    /// <summary>
    /// A member's element is the member, and so is <c>0</c>'s, which <c>default</c> lowers to; two other elements are the
    /// largest member plus one and plus two, in id order, and an element met twice is one value.
    /// </summary>
    [Fact]
    public void AMembersElementIsTheMember_AndOtherElementsAreDistinctValuesNoMemberHas()
    {
        IrValue[] arguments = [Constant("N.Color", 2), new IrSortValue("N.Color", 5), new IrSortValue("N.Color", 3), new IrSortValue("N.Color", 5), Constant("N.Color", 0)];

        Assert.Equal(["2", "9", "8", "9", "0"], Wires(Case("Colors", arguments)), StringComparer.Ordinal);
        Assert.Equal(["2", "9", "8", "9", "0"], Wires(Case("Colors", arguments, Counterpart)), StringComparer.Ordinal);
    }

    /// <summary>A <c>[Flags]</c> enum's other element is above every member's bits together, so no combination of members is it.</summary>
    [Fact]
    public void AFlagsEnumsOtherElementIsNoCombinationOfMembers()
    {
        Assert.Equal(["20", "21", "2"], Wires(Case("Flagged", [new IrSortValue("N.Bits", 4), new IrSortValue("N.Bits", 6), Constant("N.Bits", (byte)2)], Counterpart)), StringComparer.Ordinal);
        Assert.Equal(["-128"], Wires(Case("Top", [new IrSortValue("N.Signed", 4)])), StringComparer.Ordinal);
    }

    [Fact]
    public void ASignedEnumsMemberAndOtherElement()
    {
        Assert.Equal(["-5", "1"], Wires(Case("Below", [Constant("N.Negative", (short)-5), new IrSortValue("N.Negative", 8)])), StringComparer.Ordinal);
    }

    [Fact]
    public void AFrameworkEnumIsBuiltLikeAnyOther()
    {
        Assert.Equal(["4", "6"], Wires(Case("Framework", [Constant("System.StringComparison", 4), new IrSortValue("System.StringComparison", 1)])), StringComparer.Ordinal);
    }

    /// <summary>Both sides' members count, so the two sides build one value from one element.</summary>
    [Fact]
    public void TheOtherSidesMembersCount()
    {
        IrValue[] arguments = [Constant("N.Shrink", 5, Counterpart), new IrSortValue("N.Shrink", 2)];

        Assert.Equal(["5", "6"], Wires(Case("Grown", arguments, Counterpart)), StringComparer.Ordinal);
        Assert.Equal(["3", "2"], Wires(Case("Grown", arguments)), StringComparer.Ordinal);
    }

    [Fact]
    public void AnEnumFieldIsBuiltFromItsElement()
    {
        IrSortValue holder = new("N.Holder", 1);
        IrMapValue paint = new(new IrMap(new IrSort("N.Holder"), new IrSort("N.Color")), Constant("N.Color", 0), ImmutableDictionary<IrValue, IrValue>.Empty.Add(holder, new IrSortValue("N.Color", 4)));

        ReplayArguments.SideCase built = Case("Held", [holder], heap: new(StringComparer.Ordinal) { ["field.N.Holder.Paint"] = paint });

        Assert.Contains("8", Assert.Single(Wires(built)), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOtherElementPastTheUnderlyingTypesRangeIsNotConstructible()
    {
        ReplayArguments.SideCase built = Case("Edge", [new IrSortValue("N.Full", 4)]);

        Assert.Null(built.Input);
        Assert.Equal("no N.Full argument can be built for f from the model: past its members, its underlying type has no value left", built.Reason);
    }
}
