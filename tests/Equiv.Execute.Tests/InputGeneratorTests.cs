using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Execute.Inputs;

using Xunit;

using static Equiv.Execute.Tests.FakeFactory;

namespace Equiv.Execute.Tests;

public sealed class InputGeneratorTests
{
    private static List<string> Column(ExecutionTypeKind kind, int cases, ulong seed = 7) =>
        [.. InputGenerator.Generate([Parameter(kind)], seed, cases).Select(static i => Assert.Single(i.Arguments))];

    [Theory]
    [InlineData(ExecutionTypeKind.SignedByte, "0,1,-1,-128,127")]
    [InlineData(ExecutionTypeKind.UnsignedByte, "0,1,255")]
    [InlineData(ExecutionTypeKind.Signed16, "0,1,-1,-32768,32767")]
    [InlineData(ExecutionTypeKind.Unsigned16, "0,1,65535")]
    [InlineData(ExecutionTypeKind.Signed32, "0,1,-1,-2147483648,2147483647")]
    [InlineData(ExecutionTypeKind.Unsigned32, "0,1,4294967295")]
    [InlineData(ExecutionTypeKind.Signed64, "0,1,-1,-9223372036854775808,9223372036854775807")]
    [InlineData(ExecutionTypeKind.Unsigned64, "0,1,18446744073709551615")]
    [InlineData(ExecutionTypeKind.Character, "97,73,105,48,32,223,230,233,304,305,769,776,55296,56320")]
    [InlineData(ExecutionTypeKind.Text, "\"i\",\"I\",\"\\u00DF\",\"ss\",\"\\u0000\",\"\\u00AD\",\"\\u00E6\",\"ae\",\"\",null")]
    public void EdgeValuesComeFirst(ExecutionTypeKind kind, string edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        string[] expected = edges.Split(",");

        Assert.Equal(expected, Column(kind, expected.Length), StringComparer.Ordinal);
    }

    [Fact]
    public void FloatingPointEdgesAreBitPatterns()
    {
        List<string> singles = Column(ExecutionTypeKind.Binary32, 11);
        List<string> doubles = Column(ExecutionTypeKind.Binary64, 12);

        Assert.Equal(
            ["00000000", "80000000", "3F800000", "BF800000", "3DCCCCCD", "FFC00000", "7F800000", "FF800000", "7F7FFFFF", "FF7FFFFF", "00000001"],
            singles.Select(static s => s[3..^1]),
            StringComparer.Ordinal);
        Assert.Equal(
            ["0000000000000000", "8000000000000000", "3FF0000000000000", "BFF0000000000000", "3FB999999999999A", "3FD3333333333334", "FFF8000000000000", "7FF0000000000000", "FFF0000000000000", "7FEFFFFFFFFFFFFF", "FFEFFFFFFFFFFFFF", "0000000000000001"],
            doubles.Select(static d => d[3..^1]),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DecimalEdgesAreTheirFourBits()
    {
        List<string> decimals = Column(ExecutionTypeKind.DecimalNumber, 7);

        Assert.Equal(
            ["[0,0,0,0]", "[1,0,0,0]", "[1,0,0,-2147483648]", "[1,0,0,65536]", "[10,0,0,65536]", "[-1,-1,-1,0]", "[-1,-1,-1,-2147483648]"],
            decimals,
            StringComparer.Ordinal);
    }

    [Fact]
    public void RandomDecimalsHaveEitherSignAndAnyValidScale()
    {
        List<decimal> decimals = [.. Column(ExecutionTypeKind.DecimalNumber, 200).Skip(7).Select(static v => new decimal(Bits(v)))];

        Assert.Contains(decimals, static m => m < 0);
        Assert.Contains(decimals, static m => m > 0);
        Assert.True(decimals.Select(static m => m.Scale).Distinct().Skip(10).Any());
    }

    [Fact]
    public void RandomStringsMixPrintableAsciiWithEdgeCharactersUpToEightLong()
    {
        List<string> strings = [.. Column(ExecutionTypeKind.Text, 300).Skip(10).Select(Unescape)];

        Assert.All(strings, static s => Assert.InRange(s.Length, 0, 8));
        Assert.Contains(strings, static s => s.Length == 8);
        Assert.Contains(strings, static s => s.Length == 0);
        Assert.All(strings.SelectMany(static s => s), static c => Assert.True(c is >= ' ' and <= '~' || EdgeCharacters.Contains(c), ((int)c).ToString(CultureInfo.InvariantCulture)));
        Assert.Contains(strings.SelectMany(static s => s), static c => c == '~');
        Assert.Contains(strings.SelectMany(static s => s), static c => c == ' ');
        Assert.Contains(strings.SelectMany(static s => s), static c => c > '~');
    }

    [Fact]
    public void RandomValuesFollowTheEdgesAndStayInRange()
    {
        foreach (ExecutionTypeKind kind in new[] { ExecutionTypeKind.SignedByte, ExecutionTypeKind.UnsignedByte, ExecutionTypeKind.Signed16, ExecutionTypeKind.Unsigned16, ExecutionTypeKind.Signed32, ExecutionTypeKind.Unsigned32, ExecutionTypeKind.Signed64, ExecutionTypeKind.Unsigned64, ExecutionTypeKind.Character })
        {
            List<string> values = Column(kind, 200);
            Assert.Equal(200, values.Count);
            Assert.All(values, v => Assert.True(Fits(kind, v), $"{kind} {v}"));
            Assert.True(values.Distinct(StringComparer.Ordinal).Skip(20).Any(), kind.ToString());
        }

        foreach ((ExecutionTypeKind kind, string pattern) in new[] { (ExecutionTypeKind.Binary32, "^\"0x[0-9A-F]{8}\"$"), (ExecutionTypeKind.Binary64, "^\"0x[0-9A-F]{16}\"$") })
        {
            List<string> values = Column(kind, 100);
            Assert.All(values, v => Assert.Matches(pattern, v));
            Assert.True(values.Distinct(StringComparer.Ordinal).Skip(80).Any(), kind.ToString());
        }

        Assert.All(Column(ExecutionTypeKind.DecimalNumber, 100).Skip(7), static v => Assert.InRange(new decimal(Bits(v)), decimal.MinValue, decimal.MaxValue));
        Assert.All(Column(ExecutionTypeKind.Text, 100).Skip(10), static v => Assert.Matches("^\"[ -~]*\"$", v));
    }

    [Fact]
    public void TheSameSeedGivesTheSameInputs()
    {
        Assert.Equal(Column(ExecutionTypeKind.Text, 60, seed: 3), Column(ExecutionTypeKind.Text, 60, seed: 3));
        Assert.NotEqual(Column(ExecutionTypeKind.Text, 60, seed: 3), Column(ExecutionTypeKind.Text, 60, seed: 4));
    }

    [Fact]
    public void EnumsGetEveryDefinedValueAndOneUndefined()
    {
        ExecutionParameter comparison = new("System.StringComparison", ExecutionTypeKind.Enum, ["0", "1", "2", "3", "4", "5"]);

        IReadOnlyList<ExecutionInput> inputs = InputGenerator.Generate([comparison], 0, 100);

        Assert.Equal(["0", "1", "2", "3", "4", "5", "6"], inputs.Select(static i => i.Arguments[0]), StringComparer.Ordinal);
    }

    [Fact]
    public void FiniteParametersStopAtEveryCombination()
    {
        IReadOnlyList<ExecutionInput> inputs = InputGenerator.Generate([Parameter(ExecutionTypeKind.Boolean), Parameter(ExecutionTypeKind.NullOnly)], 0, 100);

        Assert.Equal(["false,null", "true,null"], inputs.Select(static i => string.Join(',', i.Arguments)), StringComparer.Ordinal);
        Assert.Single(InputGenerator.Generate([], 0, 100));
    }

    [Fact]
    public void CombinationsVaryTheFirstParameterFastestThenTurnRandom()
    {
        IReadOnlyList<ExecutionInput> inputs = InputGenerator.Generate([Parameter(ExecutionTypeKind.Boolean), Parameter(ExecutionTypeKind.UnsignedByte)], 0, 10);

        Assert.Equal(["false,0", "true,0", "false,1", "true,1", "false,255", "true,255"], inputs.Take(6).Select(static i => string.Join(',', i.Arguments)), StringComparer.Ordinal);
        Assert.Equal(10, inputs.Count);
        Assert.All(inputs.Skip(6), static i => Assert.Matches("^(false|true)$", i.Arguments[0]));
    }

    [Fact]
    public void AVastProductIsCappedByTheCaseCount()
    {
        ExecutionParameter[] strings = [.. Enumerable.Repeat(Parameter(ExecutionTypeKind.Text), 12)];

        Assert.Equal(5, InputGenerator.Generate(strings, 0, 5).Count);
    }

    [Fact]
    public void AnUnsupportedParameterHasNoInputs()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => InputGenerator.Generate([Parameter(ExecutionTypeKind.Unsupported, "System.DateTime")], 0, 1));

        Assert.Equal("System.DateTime", exception.ActualValue);
        Assert.StartsWith("No input can be built for this parameter type.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The random values for one seed, pinned: the generator is part of the report's reproducibility, since the same seed
    /// must give the same inputs on every machine and .NET version.
    /// </summary>
    [Fact]
    public void ASeedPinsTheRandomValues()
    {
        IReadOnlyList<ExecutionInput> inputs = InputGenerator.Generate(
            [Parameter(ExecutionTypeKind.Text), Parameter(ExecutionTypeKind.DecimalNumber), Parameter(ExecutionTypeKind.Binary64), Parameter(ExecutionTypeKind.Signed32)],
            42,
            10_000);

        Assert.Equal(PinnedTail, inputs.Skip(inputs.Count - 3).Select(static i => string.Join(' ', i.Arguments)), StringComparer.Ordinal);
    }

    private static readonly string[] PinnedTail =
    [
        "\"\\u0131\\u0308 }\\u0131\\u0301D\" [724825957,1263914607,-1755957424,720896] \"0xA86962B9FD22AF73\" -396575173",
        "\"Aa]\" [-361872896,-1095122503,778129485,-2146631680] \"0x4FA1B3AC3A5B21A3\" -676480176",
        "\"0\" [-1760326488,1097782901,-497125042,-2147287040] \"0xFDA531526CB49D86\" -976966696",
    ];

    private static readonly char[] EdgeCharacters = ['\u00DF', '\u00E6', '\u00E9', '\u0130', '\u0131', '\u0301', '\u0308', '\uD800', '\uDC00'];

    /// <summary>Undoes <see cref="JsonText.String"/>, whose only escapes are <c>\"</c>, <c>\\</c> and <c>\uXXXX</c>.</summary>
    private static string Unescape(string json)
    {
        System.Text.StringBuilder text = new();
        for (int i = 1; i < json.Length - 1; i++)
        {
            if (json[i] != '\\')
            {
                text.Append(json[i]);
            }
            else if (json[++i] == 'u')
            {
                text.Append((char)int.Parse(json.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 4;
            }
            else
            {
                text.Append(json[i]);
            }
        }

        return text.ToString();
    }

    private static int[] Bits(string json) => [.. json.Trim('[', ']').Split(',').Select(static b => int.Parse(b, CultureInfo.InvariantCulture))];

    private static bool Fits(ExecutionTypeKind kind, string value) => kind switch
    {
        ExecutionTypeKind.SignedByte => sbyte.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.UnsignedByte => byte.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.Signed16 => short.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.Unsigned16 or ExecutionTypeKind.Character => ushort.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.Signed32 => int.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.Unsigned32 => uint.TryParse(value, CultureInfo.InvariantCulture, out _),
        ExecutionTypeKind.Signed64 => long.TryParse(value, CultureInfo.InvariantCulture, out _),
        _ => ulong.TryParse(value, CultureInfo.InvariantCulture, out _),
    };
}
