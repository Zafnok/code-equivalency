using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-056 criterion 4, end to end on a real runtime: a pair whose two projects both run on the test host's own .NET
/// replays both sides on that one runtime, located as <c>--execute</c> locates it (<see cref="DriverReferences.Installed"/>),
/// and a counterexample still reproduces or not. It needs no .NET Framework, so it runs on Linux and Windows alike.
/// <see cref="InternalDivergentMethod_Reproduces"/> (ticket P2-052 criterion 3) replays <c>internal</c> members of an
/// <c>internal</c> type through the friend the emitted project names: on Windows the legacy side is compiled against the
/// .NET Framework 4.8 reference assemblies and runs on .NET Framework, so both runtimes are real; elsewhere both sides run
/// on the test host's .NET.
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

    private const string InternalChecked = """
        namespace N
        {
            internal class Greeter
            {
                internal string Greet(string name)
                {
                    if (name == null) { throw new System.ArgumentNullException("name"); }
                    return "Hello, " + name.ToUpper();
                }

                internal static string Shout(string name)
                {
                    if (name == null) { throw new System.ArgumentNullException("name"); }
                    return name.ToUpper() + "!";
                }
            }
        }
        """;

    private const string InternalUnchecked = """
        namespace N
        {
            internal class Greeter
            {
                internal string Greet(string name) => "Hello, " + name.ToUpper();

                protected internal static string Shout(string name) => name.ToUpper() + "!";
            }
        }
        """;

    private const string ShoutIr = "proc \"X\" (%name: sort \"System.String\", %null.System.String: map<sort \"System.String\", bool>) entry B0 B0: ret";

    private const string GreetIr = "proc \"X\" (%name: sort \"System.String\", %this: sort \"N.Greeter\", %null.System.String: map<sort \"System.String\", bool>) entry B0 B0: ret";

    private static readonly TargetRuntime Self = new(TargetRuntime.RuntimeFamily.NetCore, new Version(Environment.Version.Major, Environment.Version.Minor));

    private readonly string directory = Directory.CreateTempSubdirectory("replay-integration-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData("Greet", false)]
    [InlineData("Shout", false)]
    [InlineData("Greet", true)]
    [InlineData("Shout", true)]
    public void InternalDivergentMethod_Reproduces(string method, bool strongNamed)
    {
        DriverReferences installed = DriverReferences.Installed();
        TargetRuntime legacyRuntime = OperatingSystem.IsWindows() ? Net48 : Self;
        IEnumerable<MetadataReference> legacyReferences = OperatingSystem.IsWindows()
            ? installed.For(Net48)!.References.Select(static r => MetadataReference.CreateFromFile(r))
            : Runtime;
        CSharpCompilation legacy = Project(InternalChecked, legacyReferences, strongNamed);
        CSharpCompilation modern = Project(InternalUnchecked, Runtime, strongNamed);
        bool instance = string.Equals(method, "Greet", StringComparison.Ordinal);
        string ir = instance ? GreetIr : ShoutIr;
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            legacyRuntime, Self, installed.Host, legacy, Method(legacy, "N.Greeter", method), ir, modern, Method(modern, "N.Greeter", method), ir);
        Counterexample model = instance
            ? Counterexample(new IrSortValue("System.String", 3), new IrSortValue("N.Greeter", 1), Nulls("System.String", 3))
            : Counterexample(new IrSortValue("System.String", 3), Nulls("System.String", 3));

        ReplayPlan plan = factory.Create(pair, model, directory);
        ReplayResult result = new Replayer(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(directory)).Replay(plan, model, pair.OldBody!, pair.NewBody!);

        Assert.Empty(plan.Reason);
        Assert.EndsWith(OperatingSystem.IsWindows() ? ".exe" : ".dll", plan.Drivers!.Legacy, StringComparison.Ordinal);
        Assert.Equal(ReplayStatus.Reproduced, result.Status);
        Assert.DoesNotContain("Reflection", File.ReadAllText(Path.ChangeExtension(plan.Drivers.Legacy, ".cs")), StringComparison.Ordinal);
    }

    /// <summary>A project named <c>Greeter</c> over <paramref name="references"/>, strong-named with a key of its own when asked.</summary>
    private CSharpCompilation Project(string source, IEnumerable<MetadataReference> references, bool strongNamed)
    {
        CSharpCompilationOptions options = new(OutputKind.DynamicallyLinkedLibrary);
        return CSharpCompilation.Create(
            "Greeter",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            strongNamed
                ? options.WithCryptoKeyFile(new DriverKey().Write(Directory.CreateDirectory(Path.Combine(directory, Path.GetRandomFileName())).FullName)).WithStrongNameProvider(new DesktopStrongNameProvider())
                : options);
    }

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
