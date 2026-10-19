using System.Text.Json;

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

    /// <summary>
    /// Tickets P2-242's driver targets: a constructor, flat structs as arguments and results, and <c>IntPtr</c>; and
    /// P2-246's, a struct of auto-properties built by its constructor and nested in another struct.
    /// </summary>
    private const string Shapes = """
        namespace N
        {
            public struct V { public float X; public float Y; }
            public struct P { public readonly float X, Y; public P(float x, float y) { X = x; Y = y; } }
            public struct A { public float X { get; } public float Y { get; } public A(float x, float y) { X = x; Y = y; } }
            public struct R { public A Lo; public V Hi; }
            public class Box { public Box(string path) { if (path == null) { throw new System.ArgumentNullException("path"); } } }
            public static class C
            {
                public static P Max(P a, P b) => new P(MAX(a.X, b.X), MAX(a.Y, b.Y));
                private static float Greater(float a, float b) => a > b ? a : b;
                public static V Flip(V v) => new V { X = v.Y, Y = v.X };
                public static R Corner(A a, A b) => new R { Lo = new A(MAX(a.X, b.X), a.Y), Hi = new V { X = b.X, Y = b.Y } };
                public static System.IntPtr Handle(long x) => new System.IntPtr(x);
                public static System.UIntPtr Unsigned(ulong x) => new System.UIntPtr(x);
            }
        }
        """;

    private const string NoIr = "proc \"X\" () entry B0 B0: ret";

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

    /// <summary>
    /// Ticket P2-242, on real drivers: <c>Math.Max</c> and a comparison disagree on the sign of a zero, which only a struct
    /// argument and a struct result compared field by field can show; a constructor returns its object or throws; a struct
    /// with no <c>readonly</c> field is built by an initializer; <c>IntPtr</c> and <c>UIntPtr</c> compare as integers.
    /// Ticket P2-246: the same zero shows through auto-properties, built by their constructor, nested in a struct result.
    /// </summary>
    [Theory]
    [InlineData("N.C", "Max", "[{\"X\":0,\"Y\":1},{\"X\":-0.0,\"Y\":1}]", "Returned [\"0x00000000\",\"0x3F800000\"]", "Returned [\"0x80000000\",\"0x3F800000\"]", false)]
    [InlineData("N.C", "Flip", "[{\"X\":1,\"Y\":-0.0}]", "Returned [\"0x80000000\",\"0x3F800000\"]", "Returned [\"0x80000000\",\"0x3F800000\"]", true)]
    [InlineData("N.C", "Corner", "[{\"X\":0,\"Y\":1},{\"X\":-0.0,\"Y\":2}]", "Returned [[\"0x00000000\",\"0x3F800000\"],[\"0x80000000\",\"0x40000000\"]]", "Returned [[\"0x80000000\",\"0x3F800000\"],[\"0x80000000\",\"0x40000000\"]]", false)]
    [InlineData("N.C", "Corner", "[{\"X\":1,\"Y\":1},{\"X\":2,\"Y\":2}]", "Returned [[\"0x40000000\",\"0x3F800000\"],[\"0x40000000\",\"0x40000000\"]]", "Returned [[\"0x40000000\",\"0x3F800000\"],[\"0x40000000\",\"0x40000000\"]]", true)]
    [InlineData("N.C", "Handle", "[-5]", "Returned -5", "Returned -5", true)]
    [InlineData("N.C", "Unsigned", "[18000000000000000000]", "Returned 18000000000000000000", "Returned 18000000000000000000", true)]
    [InlineData("N.Box", ".ctor", "[null]", "Threw \"System.ArgumentNullException\"", "Threw \"System.ArgumentNullException\"", true)]
    [InlineData("N.Box", ".ctor", "[\"a\"]", "NotComparable \"N.Box\"", "NotComparable \"N.Box\"", null)]
    public void ProbeCallsConstructorsAndComparesStructs(string type, string member, string arguments, string legacyOutcome, string modernOutcome, bool? equal)
    {
        CSharpCompilation legacy = Compile(Shapes.Replace("MAX", "System.Math.Max", StringComparison.Ordinal), "Shapes");
        CSharpCompilation modern = Compile(Shapes.Replace("MAX", "Greater", StringComparison.Ordinal), "Shapes");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            Self, Self, DriverReferences.Installed().Host, legacy, Method(legacy, type, member), NoIr, modern, Method(modern, type, member), NoIr);
        using JsonDocument json = JsonDocument.Parse(arguments);

        ReplayPlan plan = factory.Probe(pair, [.. json.RootElement.EnumerateArray()], receiver: null, directory);
        (ExecutionOutcome l, ExecutionOutcome m) = new Replayer(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(directory)).Run(plan, Replayer.Culture);

        Assert.Empty(plan.Reason);
        Assert.Equal(legacyOutcome, $"{l.Kind} {l.Canonical}");
        Assert.Equal(modernOutcome, $"{m.Kind} {m.Canonical}");
        Assert.Equal(equal, Replayer.Compare(l, m).Equal);
    }
}
