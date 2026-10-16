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
