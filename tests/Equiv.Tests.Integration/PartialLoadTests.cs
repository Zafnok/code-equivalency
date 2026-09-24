using Equiv.Cli;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// M3-024 (ADR 0029 decision 1) against the real MSBuild build host: a legacy side with one C# project missing a
/// reference and one C++ project still reports the loadable project's verdicts, lists what it skipped, and exits 4.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PartialLoadTests
{
    private const string BrokenProject = "Equiv.Samples.Identical.Broken";

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void ABrokenAndANativeProjectAreSkippedAndTheRestIsReported()
    {
        string copyDir = Path.Combine(Path.GetTempPath(), "equiv-M3-024-" + Guid.NewGuid().ToString("N"));
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-024-{Guid.NewGuid():N}.sarif");
        try
        {
            string legacy = CreatePartiallyBrokenLegacyCopy(copyDir);
            string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "identical", "modern"), "*.slnx").Single();

            int exitCode = CompareCommand.Run(
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: "divergent", DryRun: false),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath));

            Assert.Equal(ExitCodes.LoadFailure, exitCode);
            Run run = SarifLog.Load(outPath).Runs[0];
            Assert.NotEmpty(run.Results);
            Assert.All(run.Results, static r => Assert.Equal("EQ001", r.RuleId));

            Invocation invocation = Assert.Single(run.Invocations);
            Assert.False(invocation.ExecutionSuccessful);
            Assert.Contains(invocation.ToolExecutionNotifications, static n => n.Level == FailureLevel.Error
                && n.Message.Text.Contains($"project '{BrokenProject}'", StringComparison.Ordinal)
                && n.Message.Text.Contains("CS0246", StringComparison.Ordinal));
            Assert.Contains(invocation.ToolExecutionNotifications, static n => n.Level == FailureLevel.Warning
                && n.Message.Text.Contains("Native", StringComparison.Ordinal));
            Assert.Contains("Equiv.Samples.Identical.Broken.Uses::Address()", run.GetProperty<List<string>>("unverified"), StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(outPath);
            DeleteBestEffort(copyDir);
        }
    }

    /// <summary>
    /// Copies <c>samples/identical/legacy</c>, adds a second net48 project whose only file uses a type no reference
    /// provides, and adds a C++ project entry to the <c>.sln</c> (the file exists but is never evaluated).
    /// </summary>
    private static string CreatePartiallyBrokenLegacyCopy(string copyDir)
    {
        string sourceDir = Path.Combine(SamplesRoot, "identical", "legacy");
        Directory.CreateDirectory(Path.Combine(copyDir, "Properties"));
        Directory.CreateDirectory(Path.Combine(copyDir, "Broken"));
        Directory.CreateDirectory(Path.Combine(copyDir, "Native"));

        foreach (string file in new[] { "Calculator.cs", "Properties/AssemblyInfo.cs", "Equiv.Samples.Identical.Legacy.csproj" })
        {
            File.Copy(Path.Combine(sourceDir, file), Path.Combine(copyDir, file));
        }

        string csproj = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Legacy.csproj"));
        const string Guid = "{A1000001-0000-0000-0000-000000000001}";
        const string AssemblyName = "<AssemblyName>Equiv.Samples.Identical</AssemblyName>";
        Assert.Contains(AssemblyName, csproj, StringComparison.Ordinal);
        string brokenCsproj = csproj
            .Replace(Guid, "{A1000001-0000-0000-0000-0000000000B1}", StringComparison.Ordinal)
            .Replace(AssemblyName, $"<AssemblyName>{BrokenProject}</AssemblyName>", StringComparison.Ordinal)
            .Replace("<Compile Include=\"Properties\\AssemblyInfo.cs\" />", string.Empty, StringComparison.Ordinal)
            .Replace("<Compile Include=\"Calculator.cs\" />", "<Compile Include=\"Uses.cs\" />", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(copyDir, "Broken", $"{BrokenProject}.csproj"), brokenCsproj);
        File.WriteAllText(
            Path.Combine(copyDir, "Broken", "Uses.cs"),
            "namespace Equiv.Samples.Identical.Broken { public class Uses { public int Address() { Missing.Thing t = null; return 1; } } }");
        File.WriteAllText(Path.Combine(copyDir, "Native", "Native.vcxproj"), "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\" />");

        string sln = File.ReadAllText(Path.Combine(sourceDir, "Equiv.Samples.Identical.Legacy.sln"));
        const string Global = "Global";
        string extraProjects =
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{BrokenProject}\", \"Broken\\{BrokenProject}.csproj\", \"{{A1000001-0000-0000-0000-0000000000B1}}\"\r\nEndProject\r\n" +
            "Project(\"{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}\") = \"Native\", \"Native\\Native.vcxproj\", \"{A1000001-0000-0000-0000-0000000000C1}\"\r\nEndProject\r\n";
        int global = sln.IndexOf(Global, StringComparison.Ordinal);

        // Both are in the build configuration, so the loader opens them (P2-013).
        const string EndSection = "\tEndGlobalSection";
        string builds = Build("{A1000001-0000-0000-0000-0000000000B1}") + Build("{A1000001-0000-0000-0000-0000000000C1}");
        int endSection = sln.LastIndexOf(EndSection, StringComparison.Ordinal);
        Assert.True(endSection > global);
        string solutionPath = Path.Combine(copyDir, "Equiv.Samples.Identical.Legacy.sln");
        File.WriteAllText(solutionPath, sln[..global] + extraProjects + sln[global..endSection] + builds + sln[endSection..]);
        return solutionPath;
    }

    private static string Build(string guid) =>
        $"\t\t{guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{guid}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n";

    /// <summary>The build host can still hold files under the copy's <c>obj/</c>; the OS temp folder is reclaimed anyway.</summary>
    private static void DeleteBestEffort(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Locked file: leave the folder behind.
        }
        catch (UnauthorizedAccessException)
        {
            // Locked file reported as access denied: leave the folder behind.
        }
    }
}
