using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-056 criterion 4, end to end on a real runtime: a pair whose two projects both run on the test host's own .NET
/// replays both sides on that one runtime, located as <c>--execute</c> locates it (<see cref="DriverReferences.Installed"/>),
/// and a counterexample still reproduces or not. It needs no .NET Framework, so it runs on Linux and Windows alike.
/// </summary>
public sealed class ReplayIntegrationTests : IDisposable
{
    private const string Checked = """
        namespace N
        {
            public class Greeter
            {
                public string Greet(string name)
                {
                    if (name == null) { throw new System.ArgumentNullException("name"); }
                    return "Hello, " + name.ToUpper();
                }
            }
        }
        """;

    private const string Unchecked = """
        namespace N
        {
            public class Greeter
            {
                public string Greet(string name) => "Hello, " + name.ToUpper();
            }
        }
        """;

    private const string GreetIr = "proc \"X\" (%name: sort \"System.String\", %this: sort \"N.Greeter\", %null.System.String: map<sort \"System.String\", bool>) entry B0 B0: ret";

    private static readonly TargetRuntime Self = new(TargetRuntime.RuntimeFamily.NetCore, new Version(Environment.Version.Major, Environment.Version.Minor));

    private readonly string directory = Directory.CreateTempSubdirectory("replay-integration-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData(Unchecked, ReplayStatus.Reproduced)]
    [InlineData(Checked, ReplayStatus.NotReproduced)]
    public void SameRuntimePairReplays(string modernSource, ReplayStatus expected)
    {
        CSharpCompilation legacy = Compile(Checked, "Greeter");
        CSharpCompilation modern = Compile(modernSource, "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            Self, Self, DriverReferences.Installed().Host, legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);
        Counterexample model = Counterexample(new IrSortValue("System.String", 3), new IrSortValue("N.Greeter", 1), Nulls("System.String", 3));

        ReplayPlan plan = factory.Create(pair, model, directory);
        ReplayResult result = new Replayer(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(directory)).Replay(plan, model, pair.OldBody!, pair.NewBody!);

        Assert.Empty(plan.Reason);
        Assert.EndsWith(".dll", plan.Drivers!.Legacy, StringComparison.Ordinal);
        Assert.EndsWith(".dll", plan.Drivers.Modern, StringComparison.Ordinal);
        Assert.Equal(expected, result.Status);
    }
}
