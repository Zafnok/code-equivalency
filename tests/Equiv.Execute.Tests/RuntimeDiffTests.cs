using System.Text.Json;

using Equiv.Core.Execution;

using Xunit;

using static Equiv.Execute.Tests.FakeFactory;

namespace Equiv.Execute.Tests;

public sealed class RuntimeDiffTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("runtime-diff-tests-").FullName;

    private readonly StringWriter output = new();

    private readonly StringWriter error = new();

    private string Report => Path.Combine(directory, "report.json");

    public void Dispose()
    {
        output.Dispose();
        error.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    private int Run(IExecutionDriverFactory factory, IDriverHost host, params string[] args) =>
        new RuntimeDiff(factory, host, output, error).Run(args, isWindows: true, directory);

    private static FakeHost Answering(string legacy, string modern) =>
        new((driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? legacy : modern);

    [Fact]
    public void NonWindows_ExitsThree()
    {
        int exit = new RuntimeDiff(new FakeFactory([]), Answering("", ""), output, error).Run(["--member", "System.String::ToUpper(", "--out", Report], isWindows: false, directory);

        Assert.Equal(RuntimeDiff.UsageError, exit);
        Assert.Equal("runtime-diff needs Windows and .NET Framework 4.8 (ADR 0035)", error.ToString().TrimEnd());
        Assert.False(File.Exists(Report));
    }

    [Fact]
    public void UnsupportedParameter_IsNotConstructible()
    {
        FakeFactory factory = new([Signature("System.DateTime::AddDays(double)", Parameter(ExecutionTypeKind.Unsupported, "System.DateTime"), Parameter(ExecutionTypeKind.Binary64))]);
        FakeHost host = Answering("[\"Returned\",1]", "[\"Returned\",1]");

        int exit = Run(factory, host, "--member", "System.DateTime::AddDays(", "--out", Report);

        Assert.Equal(RuntimeDiff.NoDivergence, exit);
        Assert.Empty(host.Starts);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Report));
        JsonElement overload = Assert.Single(report.RootElement.GetProperty("overloads").EnumerateArray());
        Assert.Equal("System.DateTime", Assert.Single(overload.GetProperty("notConstructible").EnumerateArray()).GetString());
        Assert.Equal(0, overload.GetProperty("casesRun").GetInt32());
        Assert.Contains("not constructible (System.DateTime)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADivergenceExitsOneAndIsReportedWithItsWitnesses()
    {
        FakeFactory factory = new([Signature("System.String::IndexOf(string)", Parameter(ExecutionTypeKind.Text), Parameter(ExecutionTypeKind.Text))]);

        int exit = Run(factory, Answering("[\"Returned\",1]", "[\"Returned\",-1]"), "--member", "System.String::IndexOf(", "--seed", "9", "--cases", "3", "--out", Report);

        Assert.Equal(RuntimeDiff.Divergence, exit);
        ExecutionRequest request = Assert.Single(factory.Requests);
        Assert.Equal(3, request.Inputs.Count);
        Assert.Equal(RuntimeDiff.Cultures, request.Cultures);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Report));
        JsonElement root = report.RootElement;
        Assert.Equal("System.String::IndexOf(", root.GetProperty("member").GetString());
        Assert.Equal(9UL, root.GetProperty("seed").GetUInt64());
        Assert.Equal(3, root.GetProperty("cases").GetInt32());
        Assert.Equal(["invariant", "en-US", "tr-TR", "de-DE", "ja-JP"], root.GetProperty("cultures").EnumerateArray().Select(static c => c.GetString()), StringComparer.Ordinal);
        JsonElement overload = Assert.Single(root.GetProperty("overloads").EnumerateArray());
        Assert.Equal("System.String::IndexOf(string)", overload.GetProperty("member").GetString());
        Assert.Equal(15, overload.GetProperty("casesRun").GetInt32());
        Assert.Equal(15, overload.GetProperty("divergent").GetInt32());
        Assert.Equal(0, overload.GetProperty("notComparable").GetInt32());
        Assert.Empty(overload.GetProperty("notConstructible").EnumerateArray());
        JsonElement nondeterministic = overload.GetProperty("nondeterministic");
        Assert.Equal((0, 0, 0), (nondeterministic.GetProperty("legacy").GetInt32(), nondeterministic.GetProperty("modern").GetInt32(), nondeterministic.GetProperty("both").GetInt32()));
        Assert.Equal(RuntimeComparison.MaxWitnesses, overload.GetProperty("witnesses").GetArrayLength());
        JsonElement witness = overload.GetProperty("witnesses")[0];
        Assert.Equal(["i", "i"], witness.GetProperty("input").EnumerateArray().Select(static a => a.GetString()), StringComparer.Ordinal);
        Assert.Equal("invariant", witness.GetProperty("culture").GetString());
        Assert.Equal("Returned", witness.GetProperty("legacy").GetProperty("kind").GetString());
        Assert.Equal(1, witness.GetProperty("legacy").GetProperty("value").GetInt32());
        Assert.Equal(-1, witness.GetProperty("modern").GetProperty("value").GetInt32());
        Assert.Equal(
            ["System.String::IndexOf(string): 15 cases, 15 divergent, nondeterministic legacy 0 modern 0 both 0, 0 not comparable", $"report: {Report}"],
            output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
    }

    [Fact]
    public void NondeterminismIsCountedPerSideInTheReport()
    {
        FakeFactory factory = new([Signature("System.String::GetHashCode()", Parameter(ExecutionTypeKind.Text))]);
        FakeHost host = new(static (driver, session, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? "[\"Returned\",7]" : $"[\"Returned\",{(session % 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}]");

        int exit = Run(factory, host, "--member", "System.String::GetHashCode()", "--cases", "1", "--out", Report);

        Assert.Equal(RuntimeDiff.NoDivergence, exit);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Report));
        JsonElement nondeterministic = Assert.Single(report.RootElement.GetProperty("overloads").EnumerateArray()).GetProperty("nondeterministic");
        Assert.Equal((0, 5, 0), (nondeterministic.GetProperty("legacy").GetInt32(), nondeterministic.GetProperty("modern").GetInt32(), nondeterministic.GetProperty("both").GetInt32()));
        Assert.Contains("nondeterministic legacy 0 modern 5 both 0", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOverloadThatMatchesThePrefixRuns()
    {
        FakeFactory factory = new(
        [
            Signature("System.String::ToUpper()", Parameter(ExecutionTypeKind.Text)),
            Signature("System.String::ToUpper(System.Globalization.CultureInfo)", Parameter(ExecutionTypeKind.Text), Parameter(ExecutionTypeKind.NullOnly)),
            Signature("System.String::ToUpperInvariant()", Parameter(ExecutionTypeKind.Text)),
        ]);
        FakeHost host = Answering("[\"Returned\",\"I\"]", "[\"Returned\",\"I\"]");

        int exit = Run(factory, host, "--member", "System.String::ToUpper(", "--cases", "2", "--out", Report);

        Assert.Equal(RuntimeDiff.NoDivergence, exit);
        Assert.Equal(["System.String::ToUpper()", "System.String::ToUpper(System.Globalization.CultureInfo)"], factory.Requests.Select(static r => r.Member.Value), StringComparer.Ordinal);
        Assert.Contains(Path.Combine(directory, "1", "modern.dll"), host.Starts, StringComparer.Ordinal);
    }

    [Fact]
    public void ADriverThatDoesNotCompileIsNotConstructible()
    {
        FakeFactory factory = new([Signature("System.String::Old()", Parameter(ExecutionTypeKind.Text))], broken: "System.String::Old()");

        int exit = Run(factory, Answering("", ""), "--member", "System.String::Old()", "--out", Report);

        Assert.Equal(RuntimeDiff.NoDivergence, exit);
        Assert.Contains("not constructible (error CS0619: obsolete)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoMatchingMemberIsAUsageError()
    {
        int exit = Run(new FakeFactory([]), Answering("", ""), "--member", "System.String::Nope(", "--out", Report);

        Assert.Equal(RuntimeDiff.UsageError, exit);
        Assert.Contains("no public member on both runtimes matches System.String::Nope(", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--member needs a value", "--member")]
    [InlineData("--member and --out are required", "--out", "r.json")]
    [InlineData("--member and --out are required", "--member", "System.String::ToUpper(")]
    [InlineData("--member must name a type and a member, as in System.String::IndexOf(", "--member", "System.String", "--out", "r.json")]
    [InlineData("unexpected --seed -1", "--member", "System.String::ToUpper(", "--out", "r.json", "--seed", "-1")]
    [InlineData("unexpected --cases 0", "--member", "System.String::ToUpper(", "--out", "r.json", "--cases", "0")]
    [InlineData("unexpected --verbose yes", "--member", "System.String::ToUpper(", "--out", "r.json", "--verbose", "yes")]
    public void BadArgumentsAreAUsageError(string problem, params string[] args)
    {
        Assert.Equal(RuntimeDiff.UsageError, Run(new FakeFactory([]), Answering("", ""), args));
        Assert.Equal([problem, RuntimeDiffOptions.Usage], error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    [Fact]
    public void OptionsDefaultTheSeedAndTheCaseCount()
    {
        RuntimeDiffOptions? options = RuntimeDiffOptions.Parse(["--out", "r.json", "--member", "System.String::ToUpper("], out string problem);

        Assert.Equal(new RuntimeDiffOptions("System.String::ToUpper(", 0, RuntimeDiffOptions.DefaultCases, "r.json"), options);
        Assert.Empty(problem);
        Assert.Throws<ArgumentNullException>(() => new RuntimeDiff(new FakeFactory([]), Answering("", ""), output, error).Run(null!, isWindows: true, directory));
    }
}
