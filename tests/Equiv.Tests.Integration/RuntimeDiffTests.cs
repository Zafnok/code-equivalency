using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Execute;
using Equiv.Execute.Inputs;
using Equiv.Frontend.CSharp.Execution;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// ADR 0035's second oracle on the real runtimes (ticket M3-032): drivers compiled against the installed .NET Framework
/// 4.8 targeting pack and .NET 10 reference pack, run as child processes. Windows only, like the rest of this project.
/// </summary>
public sealed class RuntimeDiffTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("runtime-diff-").FullName;

    private readonly DriverFactory factory = new();

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private RunOutcomes Run(ExecutionRequest request) =>
        new DriverRunner(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes), RuntimeDiff.CaseTimeout)
            .Run(factory.Create(request, directory), request);

    /// <summary>Proves the plumbing: both runtimes upper-case "i" to U+0130 under tr-TR.</summary>
    [Fact]
    public void ToUpper_TurkishI_Agrees()
    {
        RunOutcomes runs = Run(new ExecutionRequest(new CallIdentity("System.String::ToUpper()"), [new ExecutionInput(["\"i\""])], ["tr-TR"]));

        Assert.All([runs.Legacy1, runs.Legacy2, runs.Modern1, runs.Modern2], static run =>
        {
            ExecutionOutcome outcome = Assert.Single(run);
            Assert.Equal(OutcomeKind.Returned, outcome.Kind);
            Assert.Equal("\"\\u0130\"", outcome.Canonical);
        });
        Assert.Equal(0, RuntimeComparison.Compare("System.String::ToUpper()", runs).Divergent);
    }

    /// <summary>
    /// ICU vs NLS: NLS expands "ß" to "ss", so "ss".IndexOf("ß") is 0 on .NET Framework; ICU tells them apart at the
    /// default strength, so it is -1 on .NET 10. (Microsoft's own example, "\r\n".IndexOf("\n"), returns 1 on both
    /// runtimes on current Windows ICU; see the ticket's Notes.)
    /// </summary>
    [Fact]
    public void IndexOf_SharpSInSs_Diverges()
    {
        RunOutcomes runs = Run(new ExecutionRequest(new CallIdentity("System.String::IndexOf(string)"), [new ExecutionInput(["\"ss\"", "\"\\u00DF\""])], ["en-US"]));

        OverloadReport report = RuntimeComparison.Compare("System.String::IndexOf(string)", runs);

        Assert.Equal(1, report.Divergent);
        OverloadReport.Witness witness = Assert.Single(report.Witnesses);
        Assert.Equal((OutcomeKind.Returned, "0"), (witness.Legacy.Kind, witness.Legacy.Canonical));
        Assert.Equal((OutcomeKind.Returned, "-1"), (witness.Modern.Kind, witness.Modern.Canonical));
    }

    /// <summary>String hash codes are randomised per process on .NET 10 and fixed on .NET Framework 4.8.</summary>
    [Fact]
    public void GetHashCode_IsNondeterministicOnModernOnly()
    {
        ExecutionSignature signature = Assert.Single(factory.Resolve("System.String::GetHashCode()"));
        ExecutionRequest request = new(signature.Member, InputGenerator.Generate(signature.Parameters, 0, 12), ["invariant"]);

        OverloadReport report = RuntimeComparison.Compare(signature.Member.Value, Run(request));

        Assert.NotEqual(0, report.ModernNondeterministic);
        Assert.Equal(0, report.LegacyNondeterministic);
        Assert.Equal(0, report.BothNondeterministic);
        Assert.Equal(0, report.Divergent);
    }
}
