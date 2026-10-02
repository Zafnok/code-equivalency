using System.Text.Json;

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

    private static ProbeArguments.SideCase Case(string type, string member, params string[] json) =>
        ProbeArguments.Case(Method(Project, type, member), [.. json.Select(Json)]);

    private static ProbeArguments.SideCase AllCase(int replaceIndex, string replacement)
    {
        string[] json = [.. AllJson];
        json[replaceIndex] = replacement;
        return Case("N.C", "All", json);
    }

    [Fact]
    public void AllSupportedKinds_BuildTheirWireForm()
    {
        ProbeArguments.SideCase result = Case("N.C", "All", AllJson);

        Assert.Empty(result.Reason);
        Assert.Equal(AllWire, result.Input!.Arguments);
    }

    [Fact]
    public void EnumByItsUnderlyingNumber_BothSignedAndUnsigned()
    {
        ProbeArguments.SideCase signed = AllCase(15, "0");
        ProbeArguments.SideCase unsigned = AllCase(16, "\"B\"");

        Assert.Equal("0", signed.Input!.Arguments[15]);
        Assert.Equal("1", unsigned.Input!.Arguments[16]);
    }

    [Fact]
    public void EnumUnknownName_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(15, "\"NoSuchMember\"");

        Assert.Null(result.Input);
        Assert.Contains("e", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumWrongJsonKind_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(15, "true");

        Assert.Null(result.Input);
    }

    [Fact]
    public void Boolean_WrongJsonKind_IsUnconstructible()
    {
        ProbeArguments.SideCase result = Case("N.C", "Flag", "1");

        Assert.Null(result.Input);
        Assert.Contains("b", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_WrongJsonKind_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(13, "1");

        Assert.Null(result.Input);
        Assert.Contains("s", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NullOnly_NonNull_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(14, "1");

        Assert.Null(result.Input);
    }

    [Fact]
    public void NumberTooLargeForTheWireForm_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(3, "999999999999999999999");

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
        ProbeArguments.SideCase result = AllCase(replaceIndex, wrongKindJson);

        Assert.Null(result.Input);
    }

    [Fact]
    public void DecimalTooLargeForTheWireForm_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(12, "1e30");

        Assert.Null(result.Input);
    }

    [Fact]
    public void SignedIntegerFractionalNumber_IsUnconstructible()
    {
        ProbeArguments.SideCase result = AllCase(2, "1.5");

        Assert.Null(result.Input);
    }

    [Theory]
    [InlineData(15, "1.5")] // enum E (signed)
    [InlineData(16, "1.5")] // enum U (unsigned)
    public void EnumFractionalNumber_IsUnconstructible(int replaceIndex, string fractional)
    {
        ProbeArguments.SideCase result = AllCase(replaceIndex, fractional);

        Assert.Null(result.Input);
    }

    [Fact]
    public void Boolean_False_BuildsItsWireForm()
    {
        ProbeArguments.SideCase result = Case("N.C", "Flag", "false");

        Assert.Equal("false", Assert.Single(result.Input!.Arguments));
    }

    [Fact]
    public void Text_Null_IsWireNull()
    {
        ProbeArguments.SideCase result = AllCase(13, "null");

        Assert.Equal("null", result.Input!.Arguments[13]);
    }

    [Fact]
    public void Text_ContainingQuotesBackslashesAndControlCharacters_IsEscaped()
    {
        ProbeArguments.SideCase result = AllCase(13, JsonSerializer.Serialize("a\"b\\c\nd"));

        Assert.Equal("\"a\\\"b\\\\c\\u000Ad\"", result.Input!.Arguments[13]);
    }

    [Fact]
    public void UnsupportedValueType_IsAlwaysUnconstructible()
    {
        ProbeArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "When"), [Json("1")]);

        Assert.Null(result.Input);
        Assert.Contains("at", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UncallableMethod_IsAlwaysUnconstructible()
    {
        ProbeArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "ByRef"), [Json("1")]);

        Assert.Null(result.Input);
        Assert.Equal("x is passed by reference", result.Reason);
    }

    [Fact]
    public void WrongArgumentCount_IsUnconstructible()
    {
        ProbeArguments.SideCase result = Case("N.C", "TwoArgs", "1");

        Assert.Null(result.Input);
        Assert.Equal("expected 2 argument(s), got 1", result.Reason);
    }

    [Fact]
    public void NotPublic_IsUnconstructible()
    {
        ProbeArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "Hidden"), []);

        Assert.Equal("not public (private)", result.Reason);
    }

    [Fact]
    public void Generic_IsUnconstructible()
    {
        ProbeArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.C", "Generic"), [Json("1")]);

        Assert.Equal("generic", result.Reason);
    }

    [Fact]
    public void NoPublicParameterlessConstructor_IsUnconstructible()
    {
        ProbeArguments.SideCase result = ProbeArguments.Case(Method(Project, "N.Q", "M"), []);

        Assert.Equal("N.Q has no public parameterless constructor", result.Reason);
    }
}
