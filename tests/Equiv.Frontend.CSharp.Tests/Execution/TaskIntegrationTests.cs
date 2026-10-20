using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Matching;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-252 criterion 1, on real drivers: an <c>async</c> member's outcome is its task's, a result, a fault's first
/// inner exception or <c>TaskCanceledException</c> for a cancellation, whichever exception cancelled it. On Windows the
/// legacy side is compiled against the .NET Framework 4.8 reference assemblies and runs on .NET Framework, so both runtime
/// kinds run every <c>Task</c> case; elsewhere both sides run on the test host's .NET. The .NET Framework reference
/// assemblies have no <c>ValueTask</c>, so its cases run both sides on the test host's .NET.
/// </summary>
public sealed class TaskIntegrationTests : IDisposable
{
    private const string Source = """
        namespace N
        {
            using System.Threading;
            using System.Threading.Tasks;

            public static class Jobs
            {
                public static async Task<int> Twice(int x)
                {
                    await Task.Yield();
                    return checked(2 * x);
                }

                public static async Task Fault(int x)
                {
                    await Task.Yield();
                    if (x < 0) { throw new System.InvalidOperationException(); }
                }

                public static async Task<string> Cancel(CancellationToken token)
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    return "done";
                }

                private static async Task<long> Hidden(long x)
                {
                    await Task.Delay(1);
                    return x + 1;
                }
            }
        }
        """;

    private const string ValueSource = """
        namespace N
        {
            using System.Threading.Tasks;

            public static class Values
            {
                public static async ValueTask<int> Half(int x)
                {
                    await Task.Yield();
                    if (x % 2 != 0) { throw new System.ArgumentException("odd"); }
                    return x / 2;
                }
            }
        }
        """;

    private const string NoIr = "proc \"X\" () entry B0 B0: ret";

    private static readonly TargetRuntime Self = new(TargetRuntime.RuntimeFamily.NetCore, new Version(Environment.Version.Major, Environment.Version.Minor));

    private readonly string directory = Directory.CreateTempSubdirectory("task-integration-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData("Twice", "[21]", "Returned 42")]
    [InlineData("Twice", "[2147483647]", "Threw \"System.OverflowException\"")]
    [InlineData("Fault", "[1]", "Returned null")]
    [InlineData("Fault", "[-1]", "Threw \"System.InvalidOperationException\"")]
    [InlineData("Cancel", "[\"live\"]", "Returned \"done\"")]
    [InlineData("Cancel", "[\"cancelled\"]", "Threw \"System.Threading.Tasks.TaskCanceledException\"")]
    [InlineData("Hidden", "[4]", "Returned 5")]
    public void AnAsyncMembersOutcome_IsItsTasksOnBothRuntimes(string member, string arguments, string outcome)
    {
        DriverReferences installed = DriverReferences.Installed();
        TargetRuntime legacyRuntime = OperatingSystem.IsWindows() ? Net48 : Self;
        IEnumerable<MetadataReference> legacyReferences = OperatingSystem.IsWindows()
            ? installed.For(Net48)!.References.Select(static r => MetadataReference.CreateFromFile(r))
            : Runtime;

        (ExecutionOutcome legacy, ExecutionOutcome modern) = Probe(Project(Source, legacyReferences), legacyRuntime, Project(Source, Runtime), installed, "N.Jobs", member, arguments);

        Assert.Equal(outcome, $"{legacy.Kind} {legacy.Canonical}");
        Assert.Equal(outcome, $"{modern.Kind} {modern.Canonical}");
    }

    [Theory]
    [InlineData("[8]", "Returned 4")]
    [InlineData("[7]", "Threw \"System.ArgumentException\"")]
    public void AValueTasksOutcome_IsItsResultOrFault(string arguments, string outcome)
    {
        DriverReferences installed = DriverReferences.Installed();

        (ExecutionOutcome legacy, ExecutionOutcome modern) = Probe(Project(ValueSource, Runtime), Self, Project(ValueSource, Runtime), installed, "N.Values", "Half", arguments);

        Assert.Equal(outcome, $"{legacy.Kind} {legacy.Canonical}");
        Assert.Equal(outcome, $"{modern.Kind} {modern.Canonical}");
    }

    private (ExecutionOutcome Legacy, ExecutionOutcome Modern) Probe(
        CSharpCompilation legacy, TargetRuntime legacyRuntime, CSharpCompilation modern, DriverReferences installed, string type, string member, string arguments)
    {
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            legacyRuntime, Self, installed.Host, legacy, Method(legacy, type, member), NoIr, modern, Method(modern, type, member), NoIr);
        using JsonDocument json = JsonDocument.Parse(arguments);

        ReplayPlan plan = factory.Probe(pair, [.. json.RootElement.EnumerateArray()], receiver: null, directory);
        Assert.True(plan.Reason.Length == 0, plan.Reason);
        return new Replayer(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(directory)).Run(plan, Replayer.Culture);
    }

    private static CSharpCompilation Project(string source, IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(
            "Tasks",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
