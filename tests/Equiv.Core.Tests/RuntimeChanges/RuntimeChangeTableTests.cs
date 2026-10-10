using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.RuntimeChanges;

using Xunit;

namespace Equiv.Core.Tests.RuntimeChanges;

/// <summary>The runtime-changes table (ticket M2-006, VERIFICATION-MODEL.md section 3; ADR 0008).</summary>
public sealed class RuntimeChangeTableTests
{
    private const int CuratedRowCount = 14;

    /// <summary>The rows with an unknown change point: ticket P2-113 placed every one, so its Notes list none.</summary>
    private const int UnknownChangePointCount = 0;

    private const string RowTag = "row: ";

    private const string ExcludedTag = "excluded: ";

    private const string CoveredTag = "covered by row ";

    private static readonly ImmutableHashSet<string> AllowedReasons =
        ["not a BCL member", "build-time only", "not reachable from .NET Framework code", "configuration only"];

    private static readonly TargetRuntime Boundary = TargetRuntime.Parse("netcoreapp1.0")!;

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
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.True(table.TryMatch(new CallIdentity(identityValue), table.Coverage, out RuntimeChange match));
        Assert.StartsWith(match.Member, identityValue, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.String::Concat(string,string)")]
    [InlineData("System.Math::Abs(int32)")]
    [InlineData("System.Int32::Parse(string)")]
    public void UnrelatedMembersDoNotMatch(string identityValue)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.False(table.TryMatch(new CallIdentity(identityValue), table.Coverage, out _));
    }

    [Fact]
    public void Table_SuppressionRemovesMatch()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.True(table.TryMatch(identity, table.Coverage, out RuntimeChange unsuppressed));

        ImmutableArray<string> suppressed = [unsuppressed.Member];
        Assert.False(table.TryMatch(identity, table.Coverage, suppressed, out _));
    }

    [Fact]
    public void SuppressingAnUnrelatedMemberDoesNotAffectOtherMatches()
    {
        CallIdentity identity = new("System.String::IndexOf(char)");
        ImmutableArray<string> suppressed = ["System.String::GetHashCode("];

        RuntimeChangeTable table = RuntimeChangeTable.Load();
        Assert.True(table.TryMatch(identity, table.Coverage, suppressed, out _));
    }

    /// <summary>
    /// Ticket P2-055: the coverage interval crosses every row, so a pair whose runtimes are not known is flagged as a
    /// .NET Framework to .NET pair is (ADR 0040 decision 1). It runs from .NET Framework 4.0 to the newest change point.
    /// </summary>
    [Fact]
    public void CoverageCrossesEveryRow()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.Equal(Interval("net40", "net10.0"), table.Coverage);
        Assert.All(table.Rows, row => Assert.True(table.TryMatch(new CallIdentity(row.Member + "x()"), table.Coverage, out _), row.Member));
    }

    /// <summary>A table with no dated row still has a non-empty coverage, up to <c>coveredFrom</c>; an older row does not shorten it.</summary>
    [Fact]
    public void CoverageEndsAtTheNewestOfTheRowsAndCoveredFrom()
    {
        RuntimeChangeTable undated = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "measured" }]""");
        RuntimeChangeTable older = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": "netcoreapp1.0", "source": "curated" }]""");
        RuntimeChangeTable newer = Parse("""
            [
              { "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": "net9.0", "source": "curated" },
              { "member": "C::D(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": "net6.0", "source": "curated" }
            ]
            """);

        Assert.Equal(Interval("net40", "netcoreapp3.0"), undated.Coverage);
        Assert.Equal(Interval("net40", "netcoreapp3.0"), older.Coverage);
        Assert.Equal(Interval("net40", "net9.0"), newer.Coverage);
    }

    [Fact]
    public void EveryRowHasASource()
    {
        ImmutableArray<RuntimeChange> rows = RuntimeChangeTable.Load().Rows;

        Assert.All(rows, static row => Assert.True(Enum.IsDefined(row.Source), $"'{row.Member}' has no known source"));
        Assert.Equal(CuratedRowCount, rows.Count(static row => row.Source == RuntimeChangeSource.Curated));
        Assert.Contains(rows, static row => row.Source == RuntimeChangeSource.Documented);
        Assert.Equal(rows.Length, rows.Select(static row => (row.Member, row.ChangedIn)).Distinct().Count());
    }

    [Theory]
    [InlineData("""{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "guessed" }""", "unknown source 'guessed'")]
    [InlineData("""{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null }""", "has no source")]
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
        RuntimeChangeTable table = Parse($$"""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "{{source}}" }]""");

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
                "url": "https://learn.microsoft.com/x", "changedIn": null,
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
        RuntimeChangeTable table = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated" }]""");

        Assert.Null(Assert.Single(table.Rows).Witness);
    }

    /// <summary>Tickets P2-054 and P2-113: every row has a change point, documented or measured between adjacent runtimes.</summary>
    [Fact]
    public void EveryRowHasAParseableChangedIn()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.Equal(TargetRuntime.Parse("netcoreapp3.0"), table.CoveredFrom);
        Assert.InRange(table.Rows.Count(static row => row.ChangedIn is null), 0, UnknownChangePointCount);
        Assert.All(
            table.Rows.Where(static row => row.ChangedIn is not null),
            static row => Assert.True(row.ChangedIn >= Boundary, $"'{row.Member}' changed before .NET existed"));
    }

    /// <summary>
    /// Ticket P2-113: a member whose behaviour changed at two runtimes has one row per change point, and a pair is
    /// flagged by the row it crosses: <c>StreamReader(string, Encoding)</c> changed in .NET 7 and again in .NET 10.
    /// </summary>
    [Fact]
    public void SplitRowAppliesAtEachOfItsChangePoints()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        CallIdentity identity = new("System.IO.StreamReader::.ctor(string,System.Text.Encoding)");

        Assert.True(table.TryMatch(identity, Interval("net6.0", "net7.0"), out RuntimeChange first));
        Assert.Equal(TargetRuntime.Parse("net7.0"), first.ChangedIn);
        Assert.True(table.TryMatch(identity, Interval("net9.0", "net10.0"), out RuntimeChange second));
        Assert.Equal(TargetRuntime.Parse("net10.0"), second.ChangedIn);
        Assert.False(table.TryMatch(identity, Interval("net7.0", "net9.0"), out _));
        Assert.False(table.TryMatch(identity, Interval("net48", "net6.0"), out _));
    }

    [Fact]
    public void RowAppliesOnlyInsideTheInterval()
    {
        RuntimeChangeTable table = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": "net9.0", "source": "curated" }]""");
        CallIdentity identity = new("A::B(int32)");

        Assert.True(table.TryMatch(identity, Interval("net8.0", "net10.0"), out RuntimeChange match));
        Assert.Equal(TargetRuntime.Parse("net9.0"), match.ChangedIn);
        Assert.True(table.TryMatch(identity, Interval("net48", "net9.0"), out _));
        Assert.False(table.TryMatch(identity, Interval("net9.0", "net10.0"), out _));
        Assert.False(table.TryMatch(identity, Interval("net48", "net8.0"), out _));
        Assert.False(table.TryMatch(identity, Interval("net8.0", "net10.0"), ["A::B("], out _));
        Assert.False(table.TryMatch(new CallIdentity("C::D()"), Interval("net8.0", "net10.0"), out _));
    }

    [Fact]
    public void UnknownChangePointAppliesWhenRuntimesDiffer()
    {
        RuntimeChangeTable table = Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "measured" }]""");
        CallIdentity identity = new("A::B(int32)");

        Assert.True(table.TryMatch(identity, Interval("net9.0", "net10.0"), out RuntimeChange match));
        Assert.Null(match.ChangedIn);
        Assert.True(table.TryMatch(identity, Interval("net48", "net472"), out _));
    }

    [Fact]
    public void SameRuntimeMatchesNothing()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        RuntimeInterval same = Interval("net10.0", "net10.0");

        Assert.All(table.Rows, row => Assert.False(table.TryMatch(new CallIdentity(row.Member + "x()"), same, out _), row.Member));
        Assert.True(table.TryMatch(new CallIdentity("System.String::IndexOf(char)"), Interval("net48", "net10.0"), out _));
    }

    /// <summary>Ticket P2-075 criterion 2: the <c>ListViewGroup</c> rows name <c>Add</c> and <c>Insert</c>, not the collection's other members.</summary>
    [Theory]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::Add(System.Windows.Forms.ListViewGroup)", true)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::Add(string,string)", true)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::Insert(int32,System.Windows.Forms.ListViewGroup)", true)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::GetEnumerator()", false)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::get_Count()", false)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::AddRange(System.Windows.Forms.ListViewGroup[])", false)]
    [InlineData("System.Windows.Forms.ListViewGroupCollection::Remove(System.Windows.Forms.ListViewGroup)", false)]
    public void ListViewGroupRowsMatchOnlyAddAndInsert(string identityValue, bool matches)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.Equal(matches, table.TryMatch(new CallIdentity(identityValue), table.Coverage, out _));
    }

    /// <summary>
    /// Ticket P2-075 criterion 1: a call that passes an ordinal <c>StringComparison</c> as a constant is not matched by a
    /// culture-comparison row, and is matched by every other row, and by the culture-comparison row when the comparison is
    /// not ordinal.
    /// </summary>
    [Theory]
    [InlineData("System.String::IndexOf(string,System.StringComparison)")]
    [InlineData("System.String::LastIndexOf(string,System.StringComparison)")]
    [InlineData("System.String::StartsWith(string,System.StringComparison)")]
    [InlineData("System.String::EndsWith(string,System.StringComparison)")]
    [InlineData("System.String::Compare(string,string,System.StringComparison)")]
    [InlineData("System.String::CompareTo(string)")]
    [InlineData("System.String::Equals(string,System.StringComparison)")]
    [InlineData("System.String::Equals(string,string,System.StringComparison)")]
    public void OrdinalComparisonIsNotMatchedByACultureComparisonRow(string identityValue)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        CallIdentity identity = new(identityValue);

        Assert.True(table.TryMatch(identity, table.Coverage, [], ordinalComparison: false, out RuntimeChange match));
        Assert.True(match.OrdinalUnaffected);
        Assert.False(table.TryMatch(identity, table.Coverage, [], ordinalComparison: true, out _));
    }

    [Fact]
    public void OrdinalComparisonStillMatchesARowThatIsNotAboutCultureComparison()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        CallIdentity identity = new("System.String::GetHashCode()");

        Assert.True(table.TryMatch(identity, table.Coverage, [], ordinalComparison: true, out RuntimeChange match));
        Assert.False(match.OrdinalUnaffected);
    }

    [Fact]
    public void NullIntervalThrows()
    {
        Assert.Throws<ArgumentNullException>(static () => RuntimeChangeTable.Load().TryMatch(null!, Interval("net48", "net10.0"), out _));
        Assert.Throws<ArgumentNullException>(static () => RuntimeChangeTable.Load().TryMatch(new CallIdentity("A::B()"), null!, out _));
    }

    [Theory]
    [InlineData("\"netstandard2.0\"", "malformed changedIn \"netstandard2.0\"")]
    [InlineData("5", "malformed changedIn 5")]
    public void MalformedChangedInIsRejected(string changedIn, string message)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Parse($$"""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": {{changedIn}}, "source": "curated" }]"""));

        Assert.Contains("'A::B('", exception.Message, StringComparison.Ordinal);
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingChangedInIsRejected()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(static () =>
            Parse("""[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "source": "curated" }]"""));

        Assert.Contains("'A::B(' has no changedIn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedCoveredFromIsRejected()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(static () =>
            ParseDocument("""{ "coveredFrom": "netstandard2.0", "rows": [] }"""));

        Assert.Contains("coveredFrom 'netstandard2.0'", exception.Message, StringComparison.Ordinal);
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

    /// <summary>Ticket P2-073: <c>precondition</c> is optional, and one of four names.</summary>
    [Theory]
    [InlineData("caseInsensitivePattern", RuntimeChangePrecondition.CaseInsensitivePattern)]
    [InlineData("twoDigitYearFormat", RuntimeChangePrecondition.TwoDigitYearFormat)]
    [InlineData("cultureSensitiveText", RuntimeChangePrecondition.CultureSensitiveText)]
    [InlineData("invalidPath", RuntimeChangePrecondition.InvalidPath)]
    public void PreconditionIsParsed(string name, RuntimeChangePrecondition precondition)
    {
        RuntimeChangeTable table = Parse($$"""
            [{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated", "precondition": "{{name}}" },
             { "member": "A::C(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated" }]
            """);

        Assert.Equal(precondition, table.Rows[0].Precondition);
        Assert.Null(table.Rows[1].Precondition);
    }

    [Fact]
    public void AnUnknownPreconditionIsRejected()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => Parse(
            """[{ "member": "A::B(", "reason": "r", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated", "precondition": "anything" }]"""));

        Assert.Contains("row 'A::B(' has unknown precondition 'anything'", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-073: a row with a precondition does not match a call whose constant arguments cannot reach its change,
    /// and the next row for the member is then the match. Without arguments, or with one that is no constant, it matches.
    /// </summary>
    [Fact]
    public void ARowWithAPreconditionMatchesOnlyACallThatCanReachIt()
    {
        RuntimeChangeTable table = Parse("""
            [{ "member": "A::B(", "reason": "path", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated", "precondition": "invalidPath" },
             { "member": "A::B(string,bool)", "reason": "other", "url": "https://learn.microsoft.com/x", "changedIn": null, "source": "curated" }]
            """);
        RuntimeInterval interval = Interval("net48", "net10.0");
        CallArgument valid = new("path", IsString: true, IsConstant: true, "a.txt");
        CallArgument invalid = valid with { Value = "a|b" };
        CallArgument variable = valid with { IsConstant = false, Value = null };

        Assert.False(table.TryMatch(new CallIdentity("A::B(string)"), interval, [], [valid], out _));
        Assert.True(table.TryMatch(new CallIdentity("A::B(string)"), interval, [], [invalid], out RuntimeChange reached));
        Assert.Equal("path", reached.Reason);
        Assert.True(table.TryMatch(new CallIdentity("A::B(string)"), interval, [], [variable], out _));
        Assert.True(table.TryMatch(new CallIdentity("A::B(string)"), interval, out _));
        Assert.True(table.TryMatch(new CallIdentity("A::B(string,bool)"), interval, [], [valid], out RuntimeChange next));
        Assert.Equal("other", next.Reason);
    }

    private static RuntimeInterval Interval(string first, string second) => new(TargetRuntime.Parse(first)!, TargetRuntime.Parse(second)!);

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Parses <paramref name="rows"/>, a JSON array of rows, as a table covering .NET from <c>netcoreapp3.0</c>.</summary>
    private static RuntimeChangeTable Parse(string rows) => ParseDocument($$"""{ "coveredFrom": "netcoreapp3.0", "rows": {{rows}} }""");

    private static RuntimeChangeTable ParseDocument(string json)
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
