using System.Text.Json;

using Equiv.Core.Execution;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>Ticket M5-002's <c>probe</c>: an agent's own JSON arguments read positionally against a method's parameters, with no model in play.</summary>
public sealed class ProbeArgumentsTests
{
    private const string Source = """
        namespace N
        {
            public enum E : short { A = -1, B = 0 }
            public enum U : uint { A = 0, B = 1 }

            public class C
            {
                public static int All(bool b, char c, sbyte s8, byte u8, short s16, ushort u16, int s32, uint u32, long s64, ulong u64, float f, double d, decimal m, string s, object o, E e, U u) => 0;
                public static int Flag(bool b) => 0;
                private static int Hidden() => 0;
                public static T Generic<T>(T x) => x;
                public static int ByRef(ref int x) => x;
                public static int TwoArgs(int a, int b) => 0;
                public static int When(System.DateTime at) => 0;
            }

            public class Q { public Q(int x) { } public int M() => 0; }

            public struct V { public float X; public string S; }
            public struct P { public readonly float X, Y; public P(float x, float y) { X = x; Y = y; } }
            public struct Sealed { public readonly int A; }
            public struct A { public float X { get; } public float Y { get; } public A(float x, float y) { X = x; Y = y; } }
            public struct R { public A Lo; public V Hi; }
            public static class S
            {
                public static R Wrap(R r) => r;
                public static V Open(V v) => v;
                public static P Max(P a, P b) => a;
                public static int Read(Sealed s) => s.A;
            }
        }
        """;

    private static readonly Compilation Project = Compile(Source);

    private static readonly string[] AllJson =
    [
        "true", "65", "-1", "200", "-2", "40000", "-3", "3000000000", "-4", "18000000000000000000",
        "1.5", "2.5", "3.25", "\"hi\"", "null", "\"A\"", "1",
    ];

    private static readonly string[] AllWire =
    [
        "true", "65", "-1", "200", "-2", "40000", "-3", "3000000000", "-4", "18000000000000000000",
        "\"0x3FC00000\"", "\"0x4004000000000000\"", "[325,0,0,131072]", "\"hi\"", "null", "-1", "1",
    ];

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static ReplayArguments.SideCase Case(string type, string member, params string[] json) =>
        ProbeArguments.Case(Method(Project, type, member), Project, [.. json.Select(Json)]);

    private static ReplayArguments.SideCase AllCase(int replaceIndex, string replacement)
    {
        string[] json = [.. AllJson];
        json[replaceIndex] = replacement;
        return Case("N.C", "All", json);
    }

    [Fact]
    public void AllSupportedKinds_BuildTheirWireForm()
    {
        ReplayArguments.SideCase result = Case("N.C", "All", AllJson);

        Assert.Empty(result.Reason);
        Assert.Equal(AllWire, result.Input!.Arguments);
    }

    [Fact]
    public void EnumByItsUnderlyingNumber_BothSignedAndUnsigned()
    {
        ReplayArguments.SideCase signed = AllCase(15, "0");
        ReplayArguments.SideCase unsigned = AllCase(16, "\"B\"");

        Assert.Equal("0", signed.Input!.Arguments[15]);
        Assert.Equal("1", unsigned.Input!.Arguments[16]);
    }

    [Fact]
    public void EnumUnknownName_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(15, "\"NoSuchMember\"");

        Assert.Null(result.Input);
        Assert.Contains("e", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumWrongJsonKind_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(15, "true");

        Assert.Null(result.Input);
    }

    [Fact]
    public void Boolean_WrongJsonKind_IsUnconstructible()
    {
        ReplayArguments.SideCase result = Case("N.C", "Flag", "1");

        Assert.Null(result.Input);
        Assert.Contains("b", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_WrongJsonKind_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(13, "1");

        Assert.Null(result.Input);
        Assert.Contains("s", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NullOnly_NonNull_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(14, "1");

        Assert.Null(result.Input);
    }

    [Fact]
    public void NumberTooLargeForTheWireForm_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(3, "999999999999999999999");

        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(1, "\"c\"")] // unsigned integer group (char)
    [InlineData(2, "\"s8\"")] // signed integer group
    [InlineData(10, "\"f\"")] // Binary32
    [InlineData(11, "\"d\"")] // Binary64
    [InlineData(12, "\"m\"")] // DecimalNumber
    public void NumericKinds_WrongJsonKind_IsUnconstructible(int replaceIndex, string wrongKindJson)
    {
        ReplayArguments.SideCase result = AllCase(replaceIndex, wrongKindJson);

        Assert.Null(result.Input);
    }

    [Fact]
    public void DecimalTooLargeForTheWireForm_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(12, "1e30");

        Assert.Null(result.Input);
    }

    [Fact]
    public void SignedIntegerFractionalNumber_IsUnconstructible()
    {
        ReplayArguments.SideCase result = AllCase(2, "1.5");

        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(15, "1.5")] // enum E (signed)
    [InlineData(16, "1.5")] // enum U (unsigned)
    public void EnumFractionalNumber_IsUnconstructible(int replaceIndex, string fractional)
    {
        ReplayArguments.SideCase result = AllCase(replaceIndex, fractional);

        Assert.Null(result.Input);
    }

    [Fact]
    public void Boolean_False_BuildsItsWireForm()
    {
        ReplayArguments.SideCase result = Case("N.C", "Flag", "false");

        Assert.Equal("false", Assert.Single(result.Input!.Arguments));
    }

    [Fact]
    public void Text_Null_IsWireNull()
    {
        ReplayArguments.SideCase result = AllCase(13, "null");

        Assert.Equal("null", result.Input!.Arguments[13]);
    }

    [Fact]
    public void Text_ContainingQuotesBackslashesAndControlCharacters_IsEscaped()
    {
        ReplayArguments.SideCase result = AllCase(13, JsonSerializer.Serialize("a\"b\\c\nd"));

        Assert.Equal("\"a\\\"b\\\\c\\u000Ad\"", result.Input!.Arguments[13]);
    }

    [Fact]
    public void UnsupportedValueType_IsAlwaysUnconstructible()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "When"), Project, [Json("1")]);

        Assert.Null(result.Input);
        Assert.Contains("at", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UncallableMethod_IsAlwaysUnconstructible()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "ByRef"), Project, [Json("1")]);

        Assert.Null(result.Input);
        Assert.Equal("x is passed by reference", result.Reason);
    }

    [Fact]
    public void WrongArgumentCount_IsUnconstructible()
    {
        ReplayArguments.SideCase result = Case("N.C", "TwoArgs", "1");

        Assert.Null(result.Input);
        Assert.Equal("expected 2 argument(s), got 1", result.Reason);
    }

    /// <summary>Ticket P2-245 criterion 1: a private member is a probe target, reached by its exact signature.</summary>
    [Fact]
    public void NotPublic_IsReached()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "Hidden"), Project, []);

        Assert.Empty(result.Reason);
        Assert.Empty(result.Input!.Arguments);
    }

    [Fact]
    public void Generic_IsUnconstructible()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "Generic"), Project, [Json("1")]);

        Assert.Equal("generic", result.Reason);
    }

    /// <summary>Ticket P2-245 criterion 4: with no receiver given, a solution type with no public parameterless constructor is created without one.</summary>
    [Fact]
    public void NoPublicParameterlessConstructor_IsCreatedWithoutOne()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.Q", "M"), Project, []);

        Assert.Empty(result.Reason);
        Assert.Equal(["[\"@u\",\"1\",0,[]]"], result.Input!.Arguments);
        Assert.Equal([new ReplayInput("this", "uninitialized")], result.Provenance(DriverSource.Receiver.Wire));
    }

    /// <summary>Ticket P2-242: a constructor is called like a static method, its own parameters read positionally.</summary>
    [Fact]
    public void Constructor_BuildsItsParameters()
    {
        ReplayArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.Q", ".ctor"), Project, [Json("7")]);

        Assert.Empty(result.Reason);
        Assert.Equal(["7"], result.Input!.Arguments);
    }

    /// <summary>Ticket P2-242: a flat struct is a JSON object by field name, sent as its fields in declaration order.</summary>
    [Fact]
    public void FlatStruct_IsAJsonObjectByFieldName()
    {
        ReplayArguments.SideCase open = Case("N.S", "Open", "{\"S\":\"a\",\"X\":-0.0}");
        ReplayArguments.SideCase max = Case("N.S", "Max", "{\"X\":0,\"Y\":1}", "{\"X\":-0.0,\"Y\":1}");

        Assert.Equal(["[\"0x80000000\",\"a\"]"], open.Input!.Arguments);
        Assert.Equal(["[\"0x00000000\",\"0x3F800000\"]", "[\"0x80000000\",\"0x3F800000\"]"], max.Input!.Arguments);
    }

    /// <summary>Ticket P2-246: an auto-property is a JSON member by the property's name, and a nested struct a nested object.</summary>
    [Fact]
    public void NestedStructOfAutoProperties_IsANestedJsonObject()
    {
        ReplayArguments.SideCase result = Case("N.S", "Wrap", "{\"Lo\":{\"Y\":2,\"X\":1},\"Hi\":{\"X\":3,\"S\":\"s\"}}");

        Assert.Equal(["[[\"0x3F800000\",\"0x40000000\"],[\"0x40400000\",\"s\"]]"], result.Input!.Arguments);
    }

    /// <summary>Ticket P2-246: a nested struct that is not an object with exactly its own fields cannot be built.</summary>
    [Theory]
    [InlineData("{\"Lo\":[1,2],\"Hi\":{\"X\":3,\"S\":\"s\"}}")]
    [InlineData("{\"Lo\":{\"X\":1},\"Hi\":{\"X\":3,\"S\":\"s\"}}")]
    public void NestedStruct_NotItsFields_IsUnconstructible(string json)
    {
        ReplayArguments.SideCase result = Case("N.S", "Wrap", json);

        Assert.Null(result.Input);
        Assert.StartsWith("no N.R argument can be built for r from ", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>Ticket P2-242: anything but an object with exactly the struct's fields, each of its kind, cannot be built.</summary>
    [Theory]
    [InlineData("[0,1]")]
    [InlineData("{\"X\":0}")]
    [InlineData("{\"X\":0,\"Y\":1,\"Z\":2}")]
    [InlineData("{\"X\":0,\"Z\":1}")]
    [InlineData("{\"X\":0,\"Y\":\"one\"}")]
    public void FlatStruct_NotItsFields_IsUnconstructible(string json)
    {
        ReplayArguments.SideCase result = Case("N.S", "Max", json, "{\"X\":0,\"Y\":1}");

        Assert.Null(result.Input);
        Assert.StartsWith("no N.P argument can be built for a from ", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-245: a solution struct a driver can read but not build from source (readonly, no constructor that takes
    /// its fields) is created without a constructor and given its fields, by name.
    /// </summary>
    [Fact]
    public void FlatStructWithoutABuilder_IsCreatedWithoutAConstructor()
    {
        ReplayArguments.SideCase result = Case("N.S", "Read", "{\"A\":1}");

        Assert.Equal(["[\"@s\",0,[[0,\"A\",1]]]"], result.Input!.Arguments);
        Assert.Equal([new ReplayInput("s", "uninitialized Sealed.A")], result.Provenance(DriverSource.Receiver.New));
    }
}
