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
        "validPaths": { "sources": ["S::Get("], "transforms": ["T::Join(string,"], "guards": ["G::Exists(string)"] },
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
        Assert.False(table.YieldsValidPath(new CallIdentity("T::Join(string,string)"), [Valid("path1"), Valid("path2")]));
        Assert.False(table.ValidatesPath(new CallIdentity("G::Exists(string)")));
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
    [InlineData("System.IO.FileSystemInfo::get_FullName()")]
    [InlineData("System.Windows.Forms.Application::get_UserAppDataPath()")]
    [InlineData("NUnit.Framework.TestContext::get_TestDirectory()")]
    public void TheShippedTableListsTheValidPathSources(string member) =>
        Assert.True(RuntimeChangeTable.Load().YieldsValidPath(new CallIdentity(member), []));

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

    private static RuntimeChangeTable Parse(string lists)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes($$"""{ "coveredFrom": "netcoreapp3.0", {{lists}} "rows": [] }"""));
        return RuntimeChangeTable.Parse(stream);
    }
}
