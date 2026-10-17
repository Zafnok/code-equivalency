using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Equiv.Core.RuntimeChanges;

using Xunit;

namespace Equiv.Core.Tests.RuntimeChanges;

/// <summary>
/// Ticket P2-074: the table lists the members that yield a valid path and the guards that validate one, and an operand
/// the frontend marked a valid path does not meet the path-validation rows' precondition.
/// </summary>
public sealed class ValidPathTests
{
    private const string Lists = """
        "validPaths": {
          "sources": ["S::Get("], "validators": ["V::Full(string)"], "transforms": ["T::Join(string,"], "prefixes": ["P::Start(int,int)"],
          "guards": ["G::Exists(string)"]
        },
        """;

    [Fact]
    public void AValidPathOperandDoesNotReachThePathValidationRows()
    {
        Assert.False(Reaches(RuntimeChangePrecondition.InvalidPath, Valid("path")));
        Assert.False(Reaches(RuntimeChangePrecondition.InvalidPath, Valid("path1"), Constant("path2", "a.txt")));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, Valid("path1"), Constant("path2", "a|b")));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, Valid("path1"), Variable("path2")));
    }

    /// <summary>A constant used under a guard that passed on it is a valid path whatever its text: the guard is false for a path that validation rejects.</summary>
    [Fact]
    public void AValidPathOperandThatIsAConstantDoesNotReachThem() =>
        Assert.False(Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", "a|b") with { ValidPath = true }));

    [Fact]
    public void AValidPathOperandStillReachesEveryOtherPrecondition()
    {
        Assert.True(Reaches(RuntimeChangePrecondition.CultureSensitiveText, Valid("value")));
        Assert.True(Reaches(RuntimeChangePrecondition.TwoDigitYearFormat, Valid("format")));
        Assert.True(Reaches(RuntimeChangePrecondition.CaseInsensitivePattern, Valid("pattern")));
    }

    [Fact]
    public void ASourceYieldsAValidPathWhateverItsArguments()
    {
        RuntimeChangeTable table = Parse(Lists);

        Assert.True(table.YieldsValidPath(new CallIdentity("S::Get()"), []));
        Assert.True(table.YieldsValidPath(new CallIdentity("S::Get(string)"), [Variable("name")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("S::Other()"), []));
        Assert.False(table.YieldsValidPath(new CallIdentity("G::Exists(string)"), [Valid("path")]));
    }

    [Fact]
    public void ATransformYieldsAValidPathOnlyFromArgumentsThatCannotBeInvalid()
    {
        RuntimeChangeTable table = Parse(Lists);
        CallIdentity join = new("T::Join(string,string)");

        Assert.True(table.YieldsValidPath(join, [Valid("path1"), Constant("path2", "a.txt")]));
        Assert.False(table.YieldsValidPath(join, [Valid("path1"), Constant("path2", "a|b")]));
        Assert.False(table.YieldsValidPath(join, [Valid("path1"), Variable("path2")]));
        Assert.False(table.YieldsValidPath(join, []));
        Assert.False(table.YieldsValidPath(new CallIdentity("T::Join(string[])"), [Valid("path1")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("T::Split(string,string)"), [Valid("path1"), Valid("path2")]));
    }

    /// <summary>Ticket P2-188: a validator returns only a path .NET Framework's validation accepted, whatever it was given.</summary>
    [Fact]
    public void AValidatorYieldsAValidPathWhateverItsArgument()
    {
        RuntimeChangeTable table = Parse(Lists);

        Assert.True(table.YieldsValidPath(new CallIdentity("V::Full(string)"), [Variable("path")]));
        Assert.True(table.YieldsValidPath(new CallIdentity("V::Full(string)"), [Constant("path", "a|b")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("V::Full(string,string)"), [Variable("path"), Variable("basePath")]));
        Assert.Equal(["V::Full(string)"], table.PathValidators);
    }

    /// <summary>Ticket P2-188: a prefix yields a valid path only from a valid path's start, its <c>startIndex</c> the constant 0.</summary>
    [Fact]
    public void APrefixYieldsAValidPathOnlyFromTheStartOfOne()
    {
        RuntimeChangeTable table = Parse(Lists);
        CallIdentity start = new("P::Start(int,int)");
        CallArgument length = new("length", IsString: false, IsConstant: false, Value: null);

        Assert.True(table.YieldsValidPath(start, [Valid(CallArgument.Receiver), Index(0), length]));
        Assert.False(table.YieldsValidPath(start, [Variable(CallArgument.Receiver), Index(0), length]));
        Assert.False(table.YieldsValidPath(start, [Valid("path"), Index(0), length]));
        Assert.False(table.YieldsValidPath(start, [Valid(CallArgument.Receiver), Index(1), length]));
        Assert.False(table.YieldsValidPath(start, [Valid(CallArgument.Receiver), new("startIndex", IsString: false, IsConstant: false, Value: null), length]));
        Assert.False(table.YieldsValidPath(start, [Valid(CallArgument.Receiver), length]));
        Assert.False(table.YieldsValidPath(new CallIdentity("P::Start(int)"), [Valid(CallArgument.Receiver), Index(0)]));
    }

    [Fact]
    public void AGuardIsMatchedByPrefix()
    {
        RuntimeChangeTable table = Parse(Lists);

        Assert.True(table.ValidatesPath(new CallIdentity("G::Exists(string)")));
        Assert.False(table.ValidatesPath(new CallIdentity("G::Exists(int)")));
        Assert.False(table.ValidatesPath(new CallIdentity("S::Get()")));
        Assert.False(table.ValidatesPath(new CallIdentity("T::Join(string,string)")));
    }

    [Fact]
    public void ATableWithoutTheListsKnowsNoValidPath()
    {
        RuntimeChangeTable table = Parse(string.Empty);

        Assert.False(table.YieldsValidPath(new CallIdentity("S::Get()"), []));
        Assert.False(table.YieldsValidPath(new CallIdentity("V::Full(string)"), [Variable("path")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("T::Join(string,string)"), [Valid("path1"), Valid("path2")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("P::Start(int,int)"), [Valid(CallArgument.Receiver), Index(0)]));
        Assert.False(table.ValidatesPath(new CallIdentity("G::Exists(string)")));
        Assert.Empty(table.PathValidators);
    }

    [Fact]
    public void ANullMemberIsRejected()
    {
        RuntimeChangeTable table = Parse(Lists);

        Assert.Throws<ArgumentNullException>(() => table.YieldsValidPath(null!, []));
        Assert.Throws<ArgumentNullException>(() => table.ValidatesPath(null!));
    }

    /// <summary>Criterion 1: the shipped table lists the sources, the transforms and the guards.</summary>
    [Theory]
    [InlineData("System.Reflection.Assembly::get_Location()")]
    [InlineData("System.AppContext::get_BaseDirectory()")]
    [InlineData("System.AppDomain::get_BaseDirectory()")]
    [InlineData("System.Environment::GetFolderPath(System.Environment.SpecialFolder)")]
    [InlineData("System.IO.Path::GetTempPath()")]
    [InlineData("System.IO.Path::GetRandomFileName()")]
    [InlineData("System.IO.FileSystemInfo::get_FullName()")]
    [InlineData("System.Windows.Forms.Application::get_UserAppDataPath()")]
    [InlineData("NUnit.Framework.TestContext::get_TestDirectory()")]
    public void TheShippedTableListsTheValidPathSources(string member) =>
        Assert.True(RuntimeChangeTable.Load().YieldsValidPath(new CallIdentity(member), []));

    /// <summary>
    /// Ticket P2-218: a string constant is a valid path exactly when it does not meet <c>invalidPath</c>, as a constant
    /// argument is read.
    /// </summary>
    [Theory]
    [InlineData("data.zip", true)]
    [InlineData("C:\\data", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a|b", false)]
    [InlineData("ab:c", false)]
    public void AConstantIsAValidPathWhenItDoesNotMeetInvalidPath(string text, bool valid)
    {
        Assert.Equal(valid, RuntimeChangeTable.IsValidPathConstant(text));
        Assert.Equal(!valid, Reaches(RuntimeChangePrecondition.InvalidPath, Constant("path", text)));
    }

    /// <summary>
    /// Ticket P2-208: a constant joins a valid path when it has no character validation rejects and no colon, whatever its
    /// length.
    /// </summary>
    [Theory]
    [InlineData("a.txt", true)]
    [InlineData("/private", true)]
    [InlineData("", true)]
    [InlineData("a|b", false)]
    [InlineData("a:b", false)]
    [InlineData("a	b", false)]
    public void AConstantJoinsAValidPathWithNoRejectedCharacterAndNoColon(string text, bool joins) =>
        Assert.Equal(joins, RuntimeChangeTable.JoinsValidPath(text));

    [Fact]
    public void TheShippedTableListsThePathTransformsAndTheGuards()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.True(table.YieldsValidPath(new CallIdentity("System.IO.Path::Combine(string,string)"), [Valid("path1"), Constant("path2", "Images")]));
        Assert.True(table.YieldsValidPath(new CallIdentity("System.IO.Path::Combine(string,string,string)"), [Valid("path1"), Valid("path2"), Valid("path3")]));
        Assert.True(table.YieldsValidPath(new CallIdentity("System.IO.Path::GetDirectoryName(string)"), [Valid("path")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("System.IO.Path::GetDirectoryName(string)"), [Variable("path")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("System.IO.Path::Combine(string[])"), [Valid("paths")]));
        Assert.True(table.ValidatesPath(new CallIdentity("System.IO.File::Exists(string)")));
        Assert.True(table.ValidatesPath(new CallIdentity("System.IO.Directory::Exists(string)")));
        Assert.False(table.ValidatesPath(new CallIdentity("System.IO.File::ReadAllText(string)")));
    }

    /// <summary>
    /// Ticket P2-188: the shipped table's validator is <c>Path.GetFullPath</c>, and its prefix <c>String.Substring</c>. A
    /// <c>GetFullPath</c> result is a valid path whatever its argument, which no transform's result is.
    /// </summary>
    [Fact]
    public void TheShippedTableListsTheValidatorAndThePrefix()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();

        Assert.True(table.YieldsValidPath(new CallIdentity("System.IO.Path::GetFullPath(string)"), [Variable("path")]));
        Assert.False(table.YieldsValidPath(new CallIdentity("System.IO.Path::GetFileName(string)"), [Variable("path")]));
        Assert.True(table.YieldsValidPath(new CallIdentity("System.String::Substring(int,int)"), [Valid(CallArgument.Receiver), Index(0), Variable("length") with { IsString = false }]));
        Assert.False(table.YieldsValidPath(new CallIdentity("System.String::Substring(int)"), [Valid(CallArgument.Receiver), Index(0)]));
    }

    /// <summary>
    /// Ticket P2-188's soundness condition: each validator is itself a path row, and its change is every path row's, so
    /// wherever a pair crosses a path row's change the validator's own call is checked on the same terms.
    /// </summary>
    [Fact]
    public void EveryValidatorIsAPathRowWithEveryPathRowsChange()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        ImmutableArray<RuntimeChange> pathRows = [.. table.Rows.Where(static row => row.Precondition == RuntimeChangePrecondition.InvalidPath)];

        Assert.NotEmpty(table.PathValidators);
        Assert.All(table.PathValidators, validator =>
            Assert.Single(pathRows, row => string.Equals(row.Member, validator, StringComparison.Ordinal)));
        Assert.Single(pathRows.Select(static row => row.ChangedIn).Distinct());
    }

    /// <summary>Criterion 1: the path rows refer to the lists through <c>invalidPath</c>, which the <c>StreamReader</c> row of the ticket's repro now carries too.</summary>
    [Fact]
    public void TheStreamReaderPathRowReadsItsArgument()
    {
        RuntimeChange row = Assert.Single(RuntimeChangeTable.Load().Rows, static row => string.Equals(row.Member, "System.IO.StreamReader::.ctor(string)", StringComparison.Ordinal));

        Assert.Equal(RuntimeChangePrecondition.InvalidPath, row.Precondition);
    }

    /// <summary>
    /// Ticket P2-232: an operand that is a valid path or the empty string does not reach a path row the empty string does
    /// not reach, and reaches every other row as any operand does.
    /// </summary>
    [Fact]
    public void AValidPathOrTheEmptyStringReachesOnlyTheRowsTheEmptyStringReaches()
    {
        CallArgument either = Variable("path") with { ValidPathOrEmpty = true };

        Assert.False(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [either], emptyPathUnchanged: true));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [either], emptyPathUnchanged: false));
        Assert.True(Reaches(RuntimeChangePrecondition.InvalidPath, either));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [Variable("path")], emptyPathUnchanged: true));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.CultureSensitiveText, [either], emptyPathUnchanged: true));
    }

    /// <summary>Ticket P2-232: a row's <c>emptyPathUnchanged</c> decides whether a valid path or the empty string matches it.</summary>
    [Fact]
    public void ARowMarkedEmptyPathUnchangedDoesNotMatchAValidPathOrTheEmptyString()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("""
            { "coveredFrom": "netcoreapp3.0", "rows": [
              { "member": "D::Set(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "emptyPathUnchanged": true, "reason": "r", "url": "https://example.com/" },
              { "member": "D::Get(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "reason": "r", "url": "https://example.com/" }
            ] }
            """));
        RuntimeChangeTable table = RuntimeChangeTable.Parse(stream);
        RuntimeInterval interval = new(TargetRuntime.Parse("net48")!, TargetRuntime.Parse("net10.0")!);
        ImmutableArray<CallArgument> either = [Variable("path") with { ValidPathOrEmpty = true }];

        Assert.True(table.Rows[0].EmptyPathUnchanged);
        Assert.False(table.Rows[1].EmptyPathUnchanged);
        Assert.False(table.TryMatch(new CallIdentity("D::Set(string)"), interval, [], either, out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Get(string)"), interval, [], either, out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Set(string)"), interval, [], [Variable("path")], out _));
    }

    /// <summary>
    /// Ticket P2-232: the shipped table marks the path rows whose empty-string behaviour is the same on .NET Framework and
    /// .NET (<c>SetCurrentDirectory</c> throws <c>ArgumentException</c> on both, <c>Combine</c> skips an empty part on both),
    /// and no other; and it lists the two directory separator fields.
    /// </summary>
    [Fact]
    public void TheShippedTableMarksTheRowsTheEmptyStringDoesNotReachAndListsTheSeparators()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        string[] marked = [.. table.Rows.Where(static row => row.EmptyPathUnchanged).Select(static row => row.Member).Order(StringComparer.Ordinal)];

        Assert.Equal(
            [
                "System.IO.Directory::SetCurrentDirectory(string)",
                "System.IO.Path::Combine(string,string)",
                "System.IO.Path::Combine(string,string,string)",
                "System.IO.Path::Combine(string,string,string,string)",
            ],
            marked);
        Assert.All(table.Rows.Where(static row => row.EmptyPathUnchanged), static row => Assert.Equal(RuntimeChangePrecondition.InvalidPath, row.Precondition));
        Assert.True(table.IsPathSeparator("System.IO.Path::DirectorySeparatorChar"));
        Assert.True(table.IsPathSeparator("System.IO.Path::AltDirectorySeparatorChar"));
        Assert.False(table.IsPathSeparator("System.IO.Path::VolumeSeparatorChar"));
        Assert.False(Parse(Lists).IsPathSeparator("System.IO.Path::DirectorySeparatorChar"));
        Assert.False(Parse(string.Empty).IsPathSeparator("System.IO.Path::DirectorySeparatorChar"));
    }

    /// <summary>
    /// Ticket P2-231: an operand that passed .NET Framework's character check does not reach a path row whose own check is
    /// that one, and reaches a row whose check is wider, a row with none recorded, and every other precondition.
    /// </summary>
    [Fact]
    public void AnOperandThatPassedTheCharacterCheckReachesOnlyTheRowsWhoseCheckIsWider()
    {
        CallArgument passed = Variable("path") with { ValidPathCharacters = true };

        Assert.False(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [passed], emptyPathUnchanged: false, PathCheck.PathCharacters));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [passed], emptyPathUnchanged: false, PathCheck.LimitedNormalization));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [passed], emptyPathUnchanged: true, PathCheck.FullPath));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [passed], emptyPathUnchanged: true, check: null));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [Variable("path")], emptyPathUnchanged: false, PathCheck.PathCharacters));
        Assert.True(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.CultureSensitiveText, [passed], emptyPathUnchanged: false, PathCheck.PathCharacters));
        Assert.False(RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, [Valid("path")], emptyPathUnchanged: false, PathCheck.FullPath));
    }

    /// <summary>Ticket P2-231: a row's <c>frameworkCheck</c> decides whether an operand that passed the character check matches it.</summary>
    [Fact]
    public void ARowsFrameworkCheckDecidesWhetherAnOperandThatPassedTheCharacterCheckMatchesIt()
    {
        RuntimeChangeTable table = ParseRows(
            """{ "member": "D::Join(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "frameworkCheck": "pathCharacters", "reason": "r", "url": "https://example.com/" },""",
            """{ "member": "D::Root(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "frameworkCheck": "limitedNormalization", "reason": "r", "url": "https://example.com/" },""",
            """{ "member": "D::Open(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "frameworkCheck": "fullPath", "reason": "r", "url": "https://example.com/" },""",
            """{ "member": "D::Read(string)", "source": "measured", "changedIn": "netcoreapp1.0", "precondition": "invalidPath", "reason": "r", "url": "https://example.com/" }""");
        RuntimeInterval interval = new(TargetRuntime.Parse("net48")!, TargetRuntime.Parse("net10.0")!);
        ImmutableArray<CallArgument> passed = [Variable("path") with { ValidPathCharacters = true }];

        Assert.Equal([PathCheck.PathCharacters, PathCheck.LimitedNormalization, PathCheck.FullPath, null], table.Rows.Select(static row => row.FrameworkCheck));
        Assert.False(table.TryMatch(new CallIdentity("D::Join(string)"), interval, [], passed, out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Join(string)"), interval, [], [Variable("path")], out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Root(string)"), interval, [], passed, out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Open(string)"), interval, [], passed, out _));
        Assert.True(table.TryMatch(new CallIdentity("D::Read(string)"), interval, [], passed, out _));
    }

    /// <summary>Ticket P2-231: a <c>frameworkCheck</c> that is not one of the three, or on a row that is no path row, is rejected.</summary>
    [Theory]
    [InlineData("\"precondition\": \"invalidPath\", \"frameworkCheck\": \"wildcards\",", "unknown frameworkCheck 'wildcards'")]
    [InlineData("\"precondition\": \"invalidPath\", \"frameworkCheck\": 1,", "unknown frameworkCheck '1'")]
    [InlineData("\"frameworkCheck\": \"fullPath\",", "frameworkCheck but no invalidPath precondition")]
    [InlineData("\"precondition\": \"blankFormat\", \"frameworkCheck\": \"fullPath\",", "frameworkCheck but no invalidPath precondition")]
    public void AFrameworkCheckThatIsNotOneOfTheThreeOrIsOnNoPathRowIsRejected(string fields, string message)
    {
        InvalidDataException rejected = Assert.Throws<InvalidDataException>(() => ParseRows(
            $$"""{ "member": "D::Join(string)", "source": "measured", "changedIn": "netcoreapp1.0", {{fields}} "reason": "r", "url": "https://example.com/" }"""));

        Assert.Contains(message, rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>Ticket P2-231: a character validator is matched by prefix, and a table without the list has none.</summary>
    [Fact]
    public void ACharacterValidatorIsMatchedByPrefix()
    {
        RuntimeChangeTable table = Parse(Lists.Replace("\"guards\"", "\"characterValidators\": [\"T::Join(string,\"], \"guards\"", StringComparison.Ordinal));

        Assert.True(table.PassesPathCharacterCheck(new CallIdentity("T::Join(string,string)")));
        Assert.False(table.PassesPathCharacterCheck(new CallIdentity("T::Join(string[])")));
        Assert.False(table.PassesPathCharacterCheck(new CallIdentity("V::Full(string)")));
        Assert.Equal(["T::Join(string,"], table.CharacterValidators);
        Assert.False(Parse(Lists).PassesPathCharacterCheck(new CallIdentity("T::Join(string,string)")));
        Assert.Empty(Parse(string.Empty).CharacterValidators);
        Assert.Throws<ArgumentNullException>(() => table.PassesPathCharacterCheck(null!));
    }

    /// <summary>
    /// Ticket P2-231, criterion 1: every path row of the shipped table records the check its .NET Framework member makes, as
    /// the reference source reads: <c>Path.CheckInvalidPathChars</c> alone, that and <c>NormalizePath</c> without its full
    /// check, or the full check with a <c>FileIOPermission</c> demand.
    /// </summary>
    [Fact]
    public void EveryShippedPathRowRecordsItsFrameworkCheck()
    {
        ImmutableArray<RuntimeChange> pathRows = [.. RuntimeChangeTable.Load().Rows.Where(static row => row.Precondition == RuntimeChangePrecondition.InvalidPath)];

        Assert.All(pathRows, static row => Assert.NotNull(row.FrameworkCheck));
        Assert.Equal(
            [
                "System.IO.Path::ChangeExtension(string,string)",
                "System.IO.Path::Combine(string,string)",
                "System.IO.Path::Combine(string,string,string)",
                "System.IO.Path::Combine(string,string,string,string)",
                "System.IO.Path::GetExtension(string)",
                "System.IO.Path::GetFileName(string)",
                "System.IO.Path::GetFileNameWithoutExtension(string)",
                "System.IO.Path::IsPathRooted(string)",
            ],
            Checking(pathRows, PathCheck.PathCharacters));
        Assert.Equal(["System.IO.Path::GetDirectoryName(string)", "System.IO.Path::GetPathRoot(string)"], Checking(pathRows, PathCheck.LimitedNormalization));
        Assert.Contains("System.IO.Path::GetFullPath(string)", Checking(pathRows, PathCheck.FullPath), StringComparer.Ordinal);
        Assert.Contains("System.IO.StreamReader::.ctor(string)", Checking(pathRows, PathCheck.FullPath), StringComparer.Ordinal);
        Assert.All(RuntimeChangeTable.Load().Rows.Where(static row => row.Precondition != RuntimeChangePrecondition.InvalidPath), static row => Assert.Null(row.FrameworkCheck));
    }

    /// <summary>
    /// Ticket P2-231's soundness condition, as P2-188's for a validator: each character validator is itself a path row whose
    /// check is at least the character check, and its change is every path row's, so wherever a pair crosses a path row's
    /// change the validator's own call is checked on the same terms. <c>ChangeExtension</c> is none: it does not check its
    /// extension.
    /// </summary>
    [Fact]
    public void EveryCharacterValidatorIsAPathRowWithEveryPathRowsChange()
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        ImmutableArray<RuntimeChange> pathRows = [.. table.Rows.Where(static row => row.Precondition == RuntimeChangePrecondition.InvalidPath)];

        Assert.NotEmpty(table.CharacterValidators);
        Assert.All(table.CharacterValidators, validator =>
            Assert.Contains(pathRows, row => row.Member.StartsWith(validator, StringComparison.Ordinal) && row.FrameworkCheck is not null));
        Assert.All(
            table.Rows.Where(row => table.CharacterValidators.Any(validator => row.Member.StartsWith(validator, StringComparison.Ordinal))),
            static row => Assert.Equal(RuntimeChangePrecondition.InvalidPath, row.Precondition));
        Assert.True(table.PassesPathCharacterCheck(new CallIdentity("System.IO.Path::GetFileName(string)")));
        Assert.True(table.PassesPathCharacterCheck(new CallIdentity("System.IO.Path::Combine(string,string,string)")));
        Assert.False(table.PassesPathCharacterCheck(new CallIdentity("System.IO.Path::ChangeExtension(string,string)")));
        Assert.False(table.PassesPathCharacterCheck(new CallIdentity("System.IO.Path::Combine(string[])")));
        Assert.Single(pathRows.Select(static row => row.ChangedIn).Distinct());
    }

    private static string[] Checking(ImmutableArray<RuntimeChange> rows, PathCheck check) =>
        [.. rows.Where(row => row.FrameworkCheck == check).Select(static row => row.Member).Order(StringComparer.Ordinal)];

    private static RuntimeChangeTable ParseRows(params string[] rows)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes($$"""{ "coveredFrom": "netcoreapp3.0", "rows": [ {{string.Concat(rows)}} ] }"""));
        return RuntimeChangeTable.Parse(stream);
    }

    private static bool Reaches(RuntimeChangePrecondition precondition, params CallArgument[] arguments) =>
        RuntimeChangeReach.CanReach(precondition, [.. arguments]);

    private static CallArgument Valid(string parameter) => Variable(parameter) with { ValidPath = true };

    private static CallArgument Constant(string parameter, string value) => new(parameter, IsString: true, IsConstant: true, value);

    private static CallArgument Variable(string parameter) => new(parameter, IsString: true, IsConstant: false, Value: null);

    private static CallArgument Index(int value) => new("startIndex", IsString: false, IsConstant: true, value);

    private static RuntimeChangeTable Parse(string lists)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes($$"""{ "coveredFrom": "netcoreapp3.0", {{lists}} "rows": [] }"""));
        return RuntimeChangeTable.Parse(stream);
    }
}
