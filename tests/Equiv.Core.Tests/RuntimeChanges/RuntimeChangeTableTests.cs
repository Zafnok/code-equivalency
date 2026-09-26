using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Xunit;

namespace Equiv.Core.Tests.RuntimeChanges;

/// <summary>The runtime-changes table (ticket M2-006, VERIFICATION-MODEL.md section 3; ADR 0008).</summary>
public sealed class RuntimeChangeTableTests
{
    private const int CuratedRowCount = 13;

    private const string RowTag = "row: ";

    private const string ExcludedTag = "excluded: ";

    private const string CoveredTag = "covered by row ";

    private static readonly ImmutableHashSet<string> AllowedReasons =
        ["not a BCL member", "build-time only", "not reachable from .NET Framework code", "configuration only"];

    [Fact]
    public void Table_LoadsAndValidatesRows()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.NotEmpty(table.Rows);
        Assert.Same(table, RuntimeChangeTable.Load());
        Assert.All(table.Rows, static row => Assert.StartsWith("https://learn.microsoft.com/", row.Url.OriginalString, StringComparison.Ordinal));
        Assert.All(table.Rows, static row => Assert.True(RuntimeChangeTable.IsIdentityPrefix(row.Member), $"'{row.Member}' does not parse as an identity prefix"));
    }

    [Theory]
    [InlineData("System.String::IndexOf(char)")]
    [InlineData("System.String::LastIndexOf(string,int32)")]
    [InlineData("System.String::StartsWith(string)")]
    [InlineData("System.String::EndsWith(string)")]
    [InlineData("System.String::Compare(string,string)")]
    [InlineData("System.String::CompareTo(string)")]
    [InlineData("System.String::GetHashCode()")]
    [InlineData("System.Globalization.CompareInfo::Compare(string,string)")]
    [InlineData("System.Text.Encoding::get_Default()")]
    [InlineData("System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::Serialize(System.IO.Stream,object)")]
    [InlineData("System.Double::ToString()")]
    [InlineData("System.Single::ToString()")]
    [InlineData("System.Double::Parse(string)")]
    public void Table_MatchesByPrefix(string identityValue)
    {
        Assert.True(RuntimeChangeTable.Load().TryMatch(new CallIdentity(identityValue), out RuntimeChange match));
        Assert.StartsWith(match.Member, identityValue, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.String::Concat(string,string)")]
    [InlineData("System.Math::Abs(int32)")]
    [InlineData("System.Int32::Parse(string)")]
    public void UnrelatedMembersDoNotMatch(string identityValue)
    {
        Assert.False(RuntimeChangeTable.Load().TryMatch(new CallIdentity(identityValue), out _));
    }

    [Fact]
    public void Table_SuppressionRemovesMatch()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.True(table.TryMatch(identity, out RuntimeChange unsuppressed));

        ImmutableArray<string> suppressed = [unsuppressed.Member];
        Assert.False(table.TryMatch(identity, suppressed, out _));
    }

    [Fact]
    public void SuppressingAnUnrelatedMemberDoesNotAffectOtherMatches()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        ImmutableArray<string> suppressed = ["System.String::GetHashCode("];

        Assert.True(RuntimeChangeTable.Load().TryMatch(identity, suppressed, out _));
    }

    [Fact]
    public void NullIdentityThrows()
    {
        Assert.Throws<ArgumentNullException>(static () => RuntimeChangeTable.Load().TryMatch(null!, out _));
    }

    [Fact]
    public void EveryRowHasASource()
    {
        ImmutableArray<RuntimeChange> rows = RuntimeChangeTable.Load().Rows;

        Assert.All(rows, static row => Assert.True(Enum.IsDefined(row.Source), $"'{row.Member}' has no known source"));
        Assert.Equal(CuratedRowCount, rows.Count(static row => row.Source == RuntimeChangeSource.Curated));
        Assert.Contains(rows, static row => row.Source == RuntimeChangeSource.Documented);
        Assert.Equal(rows.Length, rows.Select(static row => row.Member).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("""{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "source": "guessed" }""", "unknown source 'guessed'")]
    [InlineData("""{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x" }""", "has no source")]
    public void UnknownSourceIsRejected(string row, string message)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Parse($"[{row}]"));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("curated", RuntimeChangeSource.Curated)]
    [InlineData("documented", RuntimeChangeSource.Documented)]
    [InlineData("measured", RuntimeChangeSource.Measured)]
    public void KnownSourcesAreAccepted(string source, RuntimeChangeSource expected)
    {
        RuntimeChangeTable table = Parse($$"""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "source": "{{source}}" }]""");

        Assert.Equal(expected, Assert.Single(table.Rows).Source);
    }

    /// <summary>Ticket M3-033: a measured row carries the input, culture and both canonical outcomes that proved it.</summary>
    [Fact]
    public void MeasuredRowHasAWitness()
    {
        RuntimeChangeTable table = Parse("""
            [{
                "member": "A::B(",
                "reason": "r",
                "url": "https://learn.microsoft.com/x",
                "source": "measured",
                "witness": { "input": ["ss", "sharp-s"], "culture": "invariant", "legacy": 0, "modern": -1 }
            }]
            """);

        RuntimeChangeWitness witness = Assert.Single(table.Rows).Witness!;
        Assert.Equal(["\"ss\"", "\"sharp-s\""], witness.Input);
        Assert.Equal("invariant", witness.Culture);
        Assert.Equal("0", witness.Legacy);
        Assert.Equal("-1", witness.Modern);
    }

    /// <summary>A curated or documented row has no witness: only a measured row was proved with one.</summary>
    [Fact]
    public void ACuratedRowHasNoWitness()
    {
        RuntimeChangeTable table = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "source": "curated" }]""");

        Assert.Null(Assert.Single(table.Rows).Witness);
    }

    [Fact]
    public void ReviewFileAndTableAgree()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot, "docs", "runtime-changes-review.md"));
        HashSet<string> tableMembers = new(RuntimeChangeTable.Load().Rows.Select(static row => row.Member), StringComparer.Ordinal);
        HashSet<string> reviewed = new(StringComparer.Ordinal);
        int entries = 0;

        foreach (string line in lines.Where(static line => line.StartsWith("- ", StringComparison.Ordinal) && line.Contains(" — http", StringComparison.Ordinal)))
        {
            entries++;
            string verdict = line[(line.LastIndexOf(" — ", StringComparison.Ordinal) + " — ".Length)..];
            if (verdict.StartsWith(RowTag, StringComparison.Ordinal))
            {
                foreach (string prefix in verdict[RowTag.Length..].Split(" ; "))
                {
                    Assert.True(tableMembers.Contains(prefix), $"review row '{prefix}' is not in runtime-changes.json");
                    reviewed.Add(prefix);
                }
            }
            else
            {
                Assert.StartsWith(ExcludedTag, verdict, StringComparison.Ordinal);
                string reason = verdict[ExcludedTag.Length..];
                bool covered = reason.StartsWith(CoveredTag, StringComparison.Ordinal) && tableMembers.Contains(reason[CoveredTag.Length..]);
                Assert.True(covered || AllowedReasons.Contains(reason), $"'{reason}' is not an allowed exclusion reason");
            }
        }

        Assert.NotEqual(0, entries);
        Assert.Empty(tableMembers.Except(reviewed, StringComparer.Ordinal));
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static RuntimeChangeTable Parse(string json)
    {
        using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(json));
        return RuntimeChangeTable.Parse(stream);
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("")]
    [InlineData("::NoType")]
    [InlineData("Too::Many::Separators")]
    public void InvalidPrefixesAreRejected(string member)
    {
        Assert.False(RuntimeChangeTable.IsIdentityPrefix(member));
    }
}
