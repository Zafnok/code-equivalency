using Xunit;

namespace RuntimeChangesBackfill.Tests;

/// <summary>Ticket P2-054 acceptance criterion 2: where the backfill places each kind of row.</summary>
public sealed class BackfillTests
{
    private const string Compatibility = "https://learn.microsoft.com/en-us/dotnet/core/compatibility/";

    private static readonly string[] Table =
    [
        "// header",
        "[",
        "  {",
        "    \"member\": \"A::B(\",",
        "    \"source\": \"documented\",",
        "    \"reason\": \"r\",",
        $"    \"url\": \"{Compatibility}core-libraries/8.0/some-change\"",
        "  },",
        "  {",
        "    \"member\": \"C::D(\",",
        "    \"source\": \"measured\",",
        "    \"reason\": \"Returns -0 on .NET 10.\",",
        "    \"url\": \"https://learn.microsoft.com/en-us/dotnet/api/c.d\",",
        "    \"witness\": { \"input\": [0], \"culture\": \"invariant\", \"legacy\": 0, \"modern\": 1 }",
        "  }",
        "]",
    ];

    [Theory]
    [InlineData("documented", "r", Compatibility + "core-libraries/8.0/some-change", "net8.0")]
    [InlineData("documented", "r", Compatibility + "3.0#some-anchor", "netcoreapp3.0")]
    [InlineData("documented", "r", Compatibility + "serialization/10/xmlserializer", "net10.0")]
    [InlineData("documented", "r", Compatibility + "unsupported-apis#system", "netcoreapp1.0")]
    [InlineData("documented", "r", Compatibility + "fx-core#some-anchor", "netcoreapp1.0")]
    [InlineData("curated", "Became IEEE-compliant in .NET Core 3.0.", Compatibility + "globalization/5.0/icu", "netcoreapp3.0")]
    [InlineData("curated", "Disabled in .NET 9 by default.", "https://learn.microsoft.com/en-us/dotnet/api/x", "net9.0")]
    [InlineData("curated", "Uses ICU on .NET.", Compatibility + "globalization/5.0/icu", "net5.0")]
    [InlineData("documented", "Changed in .NET 9.", Compatibility + "core-libraries/7.0/x", "net7.0")]
    [InlineData("measured", "No longer validates invalid path characters.", "https://learn.microsoft.com/en-us/dotnet/api/x", "netcoreapp1.0")]
    [InlineData("measured", "Skips .NET Framework's upfront character validation.", "https://learn.microsoft.com/en-us/dotnet/api/x", "netcoreapp1.0")]
    [InlineData("measured", "Returns -0 on .NET 10.", "https://learn.microsoft.com/en-us/dotnet/api/x", null)]
    [InlineData("curated", "No longer validates invalid path characters.", "https://learn.microsoft.com/en-us/dotnet/api/x", null)]
    public void PlacesARowByItsSourceReasonAndUrl(string source, string reason, string page, string? expected)
    {
        Assert.Equal(expected, Backfill.ChangedIn(source, reason, page));
    }

    [Fact]
    public void InsertsChangedInAfterSourceAndWrapsTheRows()
    {
        Backfill.Result result = Backfill.Apply(Table);

        string[] expected =
        [
            "// header",
            "{",
            "  \"coveredFrom\": \"netcoreapp3.0\",",
            "  \"rows\": [",
            "    {",
            "      \"member\": \"A::B(\",",
            "      \"source\": \"documented\",",
            "      \"changedIn\": \"net8.0\",",
            "      \"reason\": \"r\",",
            $"      \"url\": \"{Compatibility}core-libraries/8.0/some-change\"",
            "    },",
            "    {",
            "      \"member\": \"C::D(\",",
            "      \"source\": \"measured\",",
            "      \"changedIn\": null,",
            "      \"reason\": \"Returns -0 on .NET 10.\",",
            "      \"url\": \"https://learn.microsoft.com/en-us/dotnet/api/c.d\",",
            "      \"witness\": { \"input\": [0], \"culture\": \"invariant\", \"legacy\": 0, \"modern\": 1 }",
            "    }",
            "  ]",
            "}",
        ];
        Assert.Equal(expected, result.Lines);
        Assert.Equal(["C::D("], result.Unplaced);
    }

    [Fact]
    public void RefusesAFileThatWasAlreadyBackfilled()
    {
        string[] backfilled = [.. Backfill.Apply(Table).Lines];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Backfill.Apply(backfilled));
        Assert.Contains("already has coveredFrom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RewritesTheFileInPlaceAndReportsUnplacedRows()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllLines(path, Table);
        try
        {
            using StringWriter output = new();

            Assert.Equal(0, Backfill.Run([path], output));
            Assert.Equal(Backfill.Apply(Table).Lines, File.ReadAllLines(path));
            Assert.Equal("null: C::D(" + Environment.NewLine, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PrintsUsageWithoutExactlyOnePath()
    {
        using StringWriter output = new();

        Assert.Equal(1, Backfill.Run([], output));
        Assert.StartsWith("usage: runtime-changes-backfill", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, Program.Main(["a", "b"]));
    }
}
