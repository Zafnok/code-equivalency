using Equiv.Cli;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-016 on real samples (ADR 0039): <c>--il-fallback</c> lowers both pairs of <c>samples/il-fallback</c> from IL,
/// and the snapshot records their verdicts (criterion 6); without the flag, <c>samples/business-layer</c>'s stdout and SARIF
/// are what they were before the ticket (criterion 1). Shares the "Console" collection with the other classes that run
/// <see cref="CompareCommand.Run"/>, which prints to the console this class redirects.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class IlFallbackSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public async Task SampleVerdictsSnapshot()
    {
        (string json, _) = Compare("il-fallback", ilFallback: true);

        SarifLog log = SarifLog.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.Equal(2, log.Runs[0].Results.Count);
        Assert.All(log.Runs[0].Results, static r => Assert.Equal("il", r.GetProperty<string>("lowering")));
        await VerifyXunit.Verifier.Verify(SarifNormalizer.Normalize(json, Path.Combine(SamplesRoot, "il-fallback")));
    }

    /// <summary>
    /// ADR 0042 (ticket P2-069): a rebound call is the same opaque when a pair is read from IL, so under <c>--il-fallback</c>
    /// <c>samples/dependency-rebinding</c> keeps its verdicts: <c>Has</c> keeps its IOperation bodies and stays
    /// Unknown, and <c>Clear</c> stays Divergent.
    /// </summary>
    [Fact]
    public void AReboundCallStaysUnknownUnderTheFlag()
    {
        (string json, _) = Compare("dependency-rebinding", ilFallback: true);

        SarifLog log = SarifLog.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        Result has = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Has(", StringComparison.Ordinal));
        Result clear = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Clear(", StringComparison.Ordinal));
        Assert.Equal(("EQ003", "operation"), (has.RuleId, has.GetProperty<string>("lowering")));
        Assert.Single(has.GetProperty<List<Dictionary<string, string>>>("reboundCalls"));
        Assert.Equal("EQ002", clear.RuleId);
    }

    /// <summary>
    /// <c>samples/business-layer/expected.sarif.json</c> is the run before this ticket, which it does not change; the stdout
    /// snapshot is that run's one line.
    /// </summary>
    [Fact]
    public async Task WithoutTheFlagOutputIsUnchanged()
    {
        (string json, string stdout) = Compare("business-layer", ilFallback: false);

        string expected = await File.ReadAllTextAsync(Path.Combine(SamplesRoot, "business-layer", "expected.sarif.json"), TestContext.Current.CancellationToken);
        Assert.Equal(expected, SarifNormalizer.Normalize(json, Path.Combine(SamplesRoot, "business-layer")));
        await VerifyXunit.Verifier.Verify(stdout);
    }

    /// <summary>
    /// Ticket P2-079, acceptance criterion 1. Two .NET Framework 4.8 projects differ only inside a lambda: one that a
    /// setter passes to a <c>Lazy</c>, and one a method passes to its argument. Read from IL, each lambda is a function
    /// pointer named by an ordinal, the same on both sides, and the fingerprint of that name made the two one shared call,
    /// so both members were Equivalent from IL. Neither is now. Each member also holds a lifted <c>int?</c> operator the
    /// legacy side spells out, as <c>samples/il-fallback</c> does: since ticket P2-067 the IOperation lowering has no
    /// opaque for a lambda, and without one the pair is never read from IL.
    /// </summary>
    [Fact]
    public void ALambdaWhoseBodyDiffersIsNotEquivalent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"equiv-P2-079-{Guid.NewGuid():N}");
        try
        {
            (string json, _) = Compare(Holder(Path.Combine(root, "legacy"), modern: false), Holder(Path.Combine(root, "modern"), modern: true), ilFallback: true);

            SarifLog log = SarifLog.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
            Result[] lambdas = [.. log.Runs[0].Results.Where(static r => r.Message.Text.Contains("::set_X(", StringComparison.Ordinal) || r.Message.Text.Contains("::Subscribe(", StringComparison.Ordinal))];
            Assert.Equal(2, lambdas.Length);
            Assert.All(lambdas, static r => Assert.Equal("EQ003", r.RuleId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A copy of <c>samples/il-fallback/legacy</c>'s project in <paramref name="directory"/> whose one class is the
    /// ticket's repro; the solution's path. The <paramref name="modern"/> side's two lambdas read <c>_x + 2</c> where the
    /// legacy side's read <c>_x + 1</c>, and it writes <c>value + 1</c> where the legacy side spells the lifted operator out.
    /// </summary>
    private static string Holder(string directory, bool modern)
    {
        string source = Path.Combine(SamplesRoot, "il-fallback", "legacy");
        Directory.CreateDirectory(Path.Combine(directory, "Properties"));
        foreach (string file in (string[])["Properties/AssemblyInfo.cs", "Equiv.Samples.IlFallback.Legacy.csproj", "Equiv.Samples.IlFallback.Legacy.sln"])
        {
            File.Copy(Path.Combine(source, file), Path.Combine(directory, file));
        }

        string addend = modern ? "2" : "1";
        string setter = modern ? "_y = value + 1;" : "if (value.HasValue) { _y = new int?(value.GetValueOrDefault() + 1); } else { _y = null; }";
        string result = modern ? "return a + 1;" : "if (a.HasValue) { return new int?(a.GetValueOrDefault() + 1); } return null;";
        File.WriteAllText(
            Path.Combine(directory, "Nullables.cs"),
            $$"""
            using System;

            namespace Equiv.Samples.IlFallback
            {
                public sealed class Holder
                {
                    private int _x;
                    private int? _y;

                    public Lazy<int> Value { get; private set; }

                    public int? X
                    {
                        get => _y;
                        set { {{setter}} Value = new Lazy<int>(() => _x + {{addend}}); }
                    }

                    public int? Subscribe(Action<Func<int>> register, int? a)
                    {
                        register(() => _x + {{addend}});
                        {{result}}
                    }
                }
            }
            """);
        return Path.Combine(directory, "Equiv.Samples.IlFallback.Legacy.sln");
    }

    private static (string Json, string StdOut) Compare(string sample, bool ilFallback) => Compare(
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single(),
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "modern"), "*.slnx").Single(),
        ilFallback);

    private static (string Json, string StdOut) Compare(string legacy, string modern, bool ilFallback)
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-016-{Guid.NewGuid():N}.sarif");
        TextWriter original = Console.Out;
        using StringWriter stdout = new();
        Console.SetOut(stdout);
        try
        {
            CompareCommand.Run(
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { IlFallback = ilFallback },
                [new CSharpFrontend()],
                new Z3Backend(),
                new FileReportSink(outPath),
                NullRunLog.Instance);
            return (File.ReadAllText(outPath), stdout.ToString());
        }
        finally
        {
            Console.SetOut(original);
            File.Delete(outPath);
        }
    }
}
