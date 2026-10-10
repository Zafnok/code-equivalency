using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

using Equiv.Core.RuntimeChanges;

using Xunit;

namespace Equiv.Core.Tests.RuntimeChanges;

/// <summary>
/// Ticket P2-073: a row's precondition, evaluated on a call's compile-time constant arguments. A constant that does not
/// meet it keeps the row from the call; a call with no operand the precondition reads, or with one that is not a
/// constant, is flagged as before.
/// </summary>
public sealed class RuntimeChangeReachTests
{
    private const int IgnoreCase = 1;

    private const int Multiline = 2;

    [Theory]
    [InlineData(@"\s+", false)]
    [InlineData("[a-z]+(?<id>i)(?:i)(?=i)(?-i)", false)]
    [InlineData("(?i)[a-z]", true)]
    [InlineData("(?mi-s)x", true)]
    [InlineData("a(?i:b)", true)]
    public void ARegexPatternReachesTheRangeRowOnlyWhenItIgnoresCaseInline(string pattern, bool reaches) =>
        Assert.Equal(reaches, Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Constant("input", "x"), Constant("pattern", pattern)));

    [Theory]
    [InlineData(0, false)]
    [InlineData(Multiline, false)]
    [InlineData(IgnoreCase, true)]
    [InlineData(IgnoreCase | Multiline, true)]
    public void ARegexPatternReachesTheRangeRowWhenItsOptionsIgnoreCase(int options, bool reaches) =>
        Assert.Equal(reaches, Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Constant("pattern", "[a-z]"), new CallArgument("options", IsString: false, IsConstant: true, options)));

    [Fact]
    public void ARegexCallWithoutAConstantPatternAndOptionsReachesTheRangeRow()
    {
        CallArgument pattern = Constant("pattern", "[a-z]");

        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Variable("pattern")));
        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, pattern, new CallArgument("options", IsString: false, IsConstant: false, Value: null)));
        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Constant("pattern", value: null)));
        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Constant("input", "x")));
        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern));
    }

    [Theory]
    [InlineData("yyyy-MM-dd", false)]
    [InlineData("yyyyy MMM", false)]
    [InlineData("HH:mm", false)]
    [InlineData("yy-MM-dd", true)]
    [InlineData("dd/MM/y", true)]
    [InlineData("yyyy yy", true)]
    [InlineData("d", true)]
    [InlineData("", true)]
    public void AFormatReachesTheTwoDigitYearRowOnlyWithAShortYearOrAStandardFormat(string format, bool reaches) =>
        Assert.Equal(reaches, Reaches(RuntimeChangePrecondition.TwoDigitYearFormat, Variable("s"), Constant("format", format)));

    [Fact]
    public void AParseWithoutAConstantFormatReachesTheTwoDigitYearRow()
    {
        Assert.True(Reaches(RuntimeChangePrecondition.TwoDigitYearFormat, Constant("s", "2020-01-01")));
        Assert.True(Reaches(RuntimeChangePrecondition.TwoDigitYearFormat, Variable("format")));
        Assert.True(Reaches(RuntimeChangePrecondition.TwoDigitYearFormat, Constant("format", value: null)));
    }

    [Theory]
    [InlineData("abc", "a1", false)]
    [InlineData("", "Z", false)]
    [InlineData("abc", "a-b", true)]
    [InlineData("\r\n", "abc", true)]
    [InlineData("stra\u00dfe", "ss", true)]
    public void TextReachesTheIcuRowsUnlessEveryStringIsAsciiLettersAndDigits(string receiver, string value, bool reaches) =>
        Assert.Equal(reaches, Reaches(RuntimeChangePrecondition.CultureSensitiveText, Constant(CallArgument.Receiver, receiver), Constant("value", value)));

    [Fact]
    public void AComparisonWithAStringThatIsNoConstantReachesTheIcuRows()
    {
        CallArgument comparison = new("comparisonType", IsString: false, IsConstant: false, Value: null);

        Assert.True(Reaches(RuntimeChangePrecondition.CultureSensitiveText, Variable(CallArgument.Receiver), Constant("value", "abc")));
        Assert.True(Reaches(RuntimeChangePrecondition.CultureSensitiveText, Constant("strA", "abc"), Constant("strB", value: null)));
        Assert.True(Reaches(RuntimeChangePrecondition.CultureSensitiveText, comparison));
        Assert.False(Reaches(RuntimeChangePrecondition.CultureSensitiveText, Constant("strA", "abc"), Constant("strB", "abd"), comparison));
    }

    [Theory]
    [InlineData(@"C:\data", false)]
    [InlineData("Images/cache_1.png", false)]
    [InlineData("a|b", true)]
    [InlineData("a\0b", true)]
    [InlineData("a<b>", true)]
    [InlineData("\"quoted\"", true)]
    [InlineData("*.txt", true)]
    [InlineData("what?", true)]
    [InlineData("data:stream", true)]
    [InlineData(":x", true)]
    [InlineData("  ", true)]
    [InlineData("", true)]
    public void APathReachesTheValidationRowsOnlyWhenTheFrameworkCouldRejectIt(string path, bool reaches) =>
        Assert.Equal(reaches, Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", path)));

    [Fact]
    public void ALongOrMissingOrVariablePathReachesTheValidationRows()
    {
        Assert.False(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", new string('a', 247))));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", new string('a', 248))));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", value: null)));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path1", "a"), Variable("path2")));
        Assert.False(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", "a.txt"), new CallArgument("append", IsString: false, IsConstant: false, Value: null)));
    }

    /// <summary>Criterion 1: the four families of rows the ticket names carry a precondition, and no other row does.</summary>
    [Fact]
    public void TheShippedTableGivesTheFourRowFamiliesAPrecondition()
    {
        ILookup<RuntimeChangePrecondition, string> members = RuntimeChangeTable.Load().Rows
            .Where(static row => row.Precondition is not null)
            .ToLookup(static row => row.Precondition!.Value, static row => row.Member);

        Assert.Equal(["System.Text.RegularExpressions.Regex::"], members[RuntimeChangePrecondition.CaseInsensitivePattern], StringComparer.Ordinal);
        Assert.Equal(
            ["System.DateTime::Parse", "System.DateTime::TryParse", "System.DateTimeOffset::Parse", "System.DateTimeOffset::TryParse"],
            members[RuntimeChangePrecondition.TwoDigitYearFormat],
            StringComparer.Ordinal);
        Assert.Equal(8, members[RuntimeChangePrecondition.CultureSensitiveText].Count());
        Assert.All(members[RuntimeChangePrecondition.CultureSensitiveText], static member => Assert.StartsWith("System.String::", member, StringComparison.Ordinal));
        Assert.Equal(30, members[RuntimeChangePrecondition.InvalidPath].Count());
        Assert.Contains("System.IO.Path::Combine(string,string)", members[RuntimeChangePrecondition.InvalidPath], StringComparer.Ordinal);
    }

    /// <summary>A measured row's witness is an input that shows the change, so as constants its strings must meet the row's precondition.</summary>
    [Fact]
    public void EveryMeasuredWitnessMeetsItsRowsPrecondition()
    {
        ImmutableArray<RuntimeChange> rows = [.. RuntimeChangeTable.Load().Rows.Where(static row => row is { Precondition: not null, Witness: not null })];

        Assert.NotEmpty(rows);
        Assert.All(rows, static row =>
        {
            CallArgument[] arguments = [.. row.Witness!.Input.Select(Text).OfType<string>().Select(static text => Constant("path", text))];
            Assert.True(Reaches(row.Precondition!.Value, arguments), row.Member);
        });
    }

    /// <summary>A witness input's string, or null when the input is not one.</summary>
    private static string? Text(string raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
    }

    private static bool Reaches(RuntimeChangePrecondition precondition, params CallArgument[] arguments) =>
        RuntimeChangeReach.CanReach(precondition, [.. arguments]);

    private static CallArgument Constant(string parameter, string? value) => new(parameter, IsString: true, IsConstant: true, value);

    private static CallArgument Variable(string parameter) => new(parameter, IsString: true, IsConstant: false, Value: null);
}
