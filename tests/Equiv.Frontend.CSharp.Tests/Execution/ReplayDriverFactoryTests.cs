using System.Text.Json;

using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket M4-009: each side's project emitted with what it needs at run time, and a driver compiled beside it that calls the
/// method with the model's inputs. Nothing here runs a driver; <c>ReplayTests</c> in Equiv.Tests.Integration does.
/// </summary>
public sealed class ReplayDriverFactoryTests : IDisposable
{
    private const string Greeter = """
        namespace N
        {
            public class Greeter
            {
                public string Greet(string name) => "Hello, " + name.ToUpper();
            }

            public class Kinds
            {
                public int Count(string s, long n) => 0;
                public int Count(string s, int n) => 0;
                public int Count(string s) => 0;
                public static int When(System.DateTime at) => 0;
                public static int Pick(System.DayOfWeek day) => 0;
            }

            public class Locked
            {
                [System.Obsolete("no", true)] public Locked() { }
                public int M() => 0;
            }
        }
        """;

    private const string GreetIr = "proc \"X\" (%name: sort \"System.String\", %this: sort \"N.Greeter\", %null.System.String: map<sort \"System.String\", bool>) entry B0 B0: ret";

    private readonly string directory = Directory.CreateTempSubdirectory("replay-factory-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static Counterexample NullName() => Counterexample(
        new IrSortValue("System.String", 3), new IrSortValue("N.Greeter", 1), Nulls("System.String", 3));

    [Fact]
    public void Create_EmitsBothProjectsAndADriverBesideEach()
    {
        CSharpCompilation legacy = Compile(Greeter, "Greeter");
        CSharpCompilation modern = Compile(Greeter, "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        ReplayPlan first = factory.Create(pair, NullName(), directory);
        ReplayPlan second = factory.Create(pair, NullName(), directory);

        Assert.Empty(first.Reason);
        Assert.Equal(["null"], first.Legacy.Arguments);
        Assert.Equal(["null"], first.Modern.Arguments);
        string legacyProject = Path.Combine(directory, "legacy", "Greeter");
        string modernProject = Path.Combine(directory, "modern", "Greeter");
        Assert.Equal(new ExecutionDrivers(Path.Combine(legacyProject, "EquivReplay1.exe"), Path.Combine(modernProject, "EquivReplay1.dll")), first.Drivers);
        Assert.Equal(new ExecutionDrivers(Path.Combine(legacyProject, "EquivReplay2.exe"), Path.Combine(modernProject, "EquivReplay2.dll")), second.Drivers);
        Assert.All(
            ["Greeter.dll", "EquivReplay1.exe", "EquivReplay1.exe.config", "EquivReplay1.cs", "System.Console.dll"],
            file => Assert.True(File.Exists(Path.Combine(legacyProject, file)), file));
        Assert.All(
            ["Greeter.dll", "EquivReplay1.dll", "EquivReplay1.runtimeconfig.json", "EquivReplay1.cs"],
            file => Assert.True(File.Exists(Path.Combine(modernProject, file)), file));
        Assert.Contains("v4.0", File.ReadAllText(Path.Combine(legacyProject, "EquivReplay1.exe.config")), StringComparison.Ordinal);
        Assert.Contains(
            "global::N.Greeter self = new global::N.Greeter();",
            File.ReadAllText(Path.Combine(modernProject, "EquivReplay1.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_HeapModel_IsNotConstructible()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        const string heap = "proc \"X\" (%name: sort \"System.String\", %field.N.Greeter.x: map<sort \"N.Greeter\", bv32>, %this: sort \"N.Greeter\") entry B0 B0: ret";
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, heap, project, greet, heap);
        IrMapValue field = new(new IrMap(new IrSort("N.Greeter"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 0), []);

        ReplayPlan plan = factory.Create(pair, Counterexample(new IrSortValue("System.String", 3), field, new IrSortValue("N.Greeter", 1)), directory);

        Assert.Null(plan.Drivers);
        Assert.Equal("legacy: the model constrains field.N.Greeter.x", plan.Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Theory]
    [InlineData(true, "emit-failed: legacy project Broken: ")]
    [InlineData(false, "emit-failed: modern project Broken: ")]
    public void Replay_EmitFailure_IsNotConstructible(bool legacyBroken, string reason)
    {
        CSharpCompilation good = Compile(Greeter, "Broken");
        CSharpCompilation broken = Compile(Greeter + "namespace N { public class Bad { public int M() => missing; } }", "Broken");
        CSharpCompilation legacy = legacyBroken ? broken : good;
        CSharpCompilation modern = legacyBroken ? good : broken;
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        ReplayPlan plan = factory.Create(pair, NullName(), directory);
        ReplayPlan again = factory.Create(pair, NullName(), directory);

        Assert.Null(plan.Drivers);
        Assert.StartsWith(reason, plan.Reason, StringComparison.Ordinal);
        Assert.Contains("CS0103", plan.Reason, StringComparison.Ordinal);
        Assert.Equal(plan.Reason, again.Reason);
    }

    [Fact]
    public void ADriverThatDoesNotCompile_IsNotConstructible()
    {
        CSharpCompilation project = Compile(Greeter, "Locked");
        IMethodSymbol m = Method(project, "N.Locked", "M");
        const string ir = "proc \"X\" (%this: sort \"N.Locked\") entry B0 B0: ret";
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, m, ir, project, m, ir);

        ReplayPlan plan = factory.Create(pair, Counterexample(new IrSortValue("N.Locked", 1)), directory);

        Assert.Null(plan.Drivers);
        Assert.StartsWith("the legacy driver does not compile: ", plan.Reason, StringComparison.Ordinal);
        Assert.Contains("CS0619", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AModernSideItCannotCall_IsNotConstructible()
    {
        CSharpCompilation legacy = Compile(Greeter, "Greeter");
        CSharpCompilation modern = Compile(Greeter.Replace("public string Greet", "internal string Greet", StringComparison.Ordinal), "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        Assert.Equal("modern: not public", factory.Create(pair, NullName(), directory).Reason);
    }

    [Fact]
    public void AModelOverOtherParameters_IsNotConstructible()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);

        Assert.Equal("the model is not over the pair's parameters", factory.Create(pair, Counterexample(), directory).Reason);
    }

    [Fact]
    public void ADivergenceInTheCallTraceOnly_IsNotConstructible()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);
        IrRun returned = new(new IrReturned(Value: null), [], []);
        Counterexample traceOnly = NullName() with { Old = returned, New = returned with { Trace = [new IrCallRecord(new Core.CallIdentity("Log::Write()"), [])] } };

        Assert.Equal("the divergence is in the call trace, which replay does not observe", factory.Create(pair, traceOnly, directory).Reason);
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, NullName(), directory));
        Assert.Throws<ArgumentNullException>(() => factory.Create(pair, null!, directory));
        Assert.Throws<ArgumentNullException>(() => factory.Plan(null!, NullName(), directory));
    }

    /// <summary>Ticket P1-008: an Unknown pair's drivers, the parameters both sides take, and its candidate counterexample as the first seed.</summary>
    [Fact]
    public void Plan_BuildsDriversParametersAndSeedsWithTheCandidate()
    {
        CSharpCompilation legacy = Compile(Greeter, "Greeter");
        CSharpCompilation modern = Compile(Greeter, "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        TestingPlan seeded = factory.Plan(pair, NullName(), directory);
        TestingPlan unseeded = factory.Plan(pair, candidate: null, directory);

        Assert.Empty(seeded.Reason);
        Assert.Equal(
            new ExecutionDrivers(Path.Combine(directory, "legacy", "Greeter", "EquivReplay1.exe"), Path.Combine(directory, "modern", "Greeter", "EquivReplay1.dll")),
            seeded.Drivers);
        Assert.Equal([("string", ExecutionTypeKind.Text)], seeded.Parameters.Select(static p => (p.TypeName, p.Kind)));
        Assert.Equal(["null"], Assert.Single(seeded.Seeds).Arguments);
        Assert.Empty(unseeded.Seeds);
        Assert.NotNull(unseeded.Drivers);
    }

    [Fact]
    public void Plan_ACandidateTheModelCannotBuild_IsNoSeed()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        const string heap = "proc \"X\" (%name: sort \"System.String\", %field.N.Greeter.x: map<sort \"N.Greeter\", bv32>, %this: sort \"N.Greeter\") entry B0 B0: ret";
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);
        (ReplayDriverFactory heapFactory, ProcedurePair heapPair) = Factory(project, greet, heap, project, greet, heap);
        IrMapValue field = new(new IrMap(new IrSort("N.Greeter"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 0), []);

        TestingPlan otherParameters = factory.Plan(pair, Counterexample(), directory);
        TestingPlan heapModel = heapFactory.Plan(heapPair, Counterexample(new IrSortValue("System.String", 3), field, new IrSortValue("N.Greeter", 1)), directory);

        Assert.NotNull(otherParameters.Drivers);
        Assert.Empty(otherParameters.Seeds);
        Assert.NotNull(heapModel.Drivers);
        Assert.Empty(heapModel.Seeds);
    }

    [Theory]
    [InlineData("Count", "Count", 0, 1, "the two sides' parameters differ: string, long and string, int")]
    [InlineData("Count", "Count", 0, 2, "the two sides' parameters differ: string, long and string")]
    [InlineData("When", "When", 0, 0, "no input can be built for System.DateTime")]
    public void Plan_ParametersItCannotGenerate_AreNotConstructible(string legacyName, string modernName, int legacyOverload, int modernOverload, string reason)
    {
        CSharpCompilation project = Compile(Greeter, "Kinds");
        INamedTypeSymbol kinds = project.GetTypeByMetadataName("N.Kinds")!;
        const string ir = "proc \"X\" () entry B0 B0: ret";
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            project, kinds.GetMembers(legacyName).OfType<IMethodSymbol>().ElementAt(legacyOverload), ir,
            project, kinds.GetMembers(modernName).OfType<IMethodSymbol>().ElementAt(modernOverload), ir);

        TestingPlan plan = factory.Plan(pair, candidate: null, directory);

        Assert.Null(plan.Drivers);
        Assert.Equal(reason, plan.Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public void Plan_EnumsTakeBothSidesValues()
    {
        CSharpCompilation project = Compile(Greeter, "Kinds");
        IMethodSymbol pick = Method(project, "N.Kinds", "Pick");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, pick, "proc \"X\" (%day: bv32) entry B0 B0: ret", project, pick, "proc \"X\" (%day: bv32) entry B0 B0: ret");

        ExecutionParameter day = Assert.Single(factory.Plan(pair, candidate: null, directory).Parameters);

        Assert.Equal((ExecutionTypeKind.Enum, 7), (day.Kind, day.EnumValues.Count));
    }

    [Theory]
    [InlineData(true, "legacy: not public")]
    [InlineData(false, "modern: not public")]
    public void Plan_ASideItCannotCall_IsNotConstructible(bool legacyHidden, string reason)
    {
        CSharpCompilation open = Compile(Greeter, "Greeter");
        CSharpCompilation hidden = Compile(Greeter.Replace("public string Greet", "internal string Greet", StringComparison.Ordinal), "Greeter");
        CSharpCompilation legacy = legacyHidden ? hidden : open;
        CSharpCompilation modern = legacyHidden ? open : hidden;
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        Assert.Equal(reason, factory.Plan(pair, NullName(), directory).Reason);
    }

    /// <summary>Ticket M5-002's <c>probe</c>: an agent's own JSON arguments build the same two drivers, with no model.</summary>
    [Fact]
    public void Probe_BuildsDriversFromTheAgentsOwnArguments()
    {
        CSharpCompilation legacy = Compile(Greeter, "Greeter");
        CSharpCompilation modern = Compile(Greeter, "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        ReplayPlan plan = factory.Probe(pair, [JsonDocument.Parse("\"Ada\"").RootElement], directory);

        Assert.Empty(plan.Reason);
        Assert.Equal(["\"Ada\""], plan.Legacy.Arguments);
        Assert.Equal(["\"Ada\""], plan.Modern.Arguments);
        Assert.Equal(
            new ExecutionDrivers(Path.Combine(directory, "legacy", "Greeter", "EquivReplay1.exe"), Path.Combine(directory, "modern", "Greeter", "EquivReplay1.dll")),
            plan.Drivers);
    }

    [Fact]
    public void Probe_WrongArgumentCount_IsNotConstructibleNamingTheSide()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);

        ReplayPlan plan = factory.Probe(pair, [], directory);

        Assert.Null(plan.Drivers);
        Assert.Equal("legacy: expected 1 argument(s), got 0", plan.Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public void Probe_AModernSideItCannotCall_IsNotConstructible()
    {
        CSharpCompilation legacy = Compile(Greeter, "Greeter");
        CSharpCompilation modern = Compile(Greeter.Replace("public string Greet", "internal string Greet", StringComparison.Ordinal), "Greeter");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        ReplayPlan plan = factory.Probe(pair, [JsonDocument.Parse("\"Ada\"").RootElement], directory);

        Assert.Equal("modern: not public", plan.Reason);
    }

    [Fact]
    public void Probe_RejectsNulls()
    {
        CSharpCompilation project = Compile(Greeter, "Greeter");
        IMethodSymbol greet = Method(project, "N.Greeter", "Greet");
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(project, greet, GreetIr, project, greet, GreetIr);

        Assert.Throws<ArgumentNullException>(() => factory.Probe(null!, [], directory));
        Assert.Throws<ArgumentNullException>(() => factory.Probe(pair, null!, directory));
    }

    [Theory]
    [InlineData(true, "emit-failed: legacy project Broken: ")]
    [InlineData(false, "emit-failed: modern project Broken: ")]
    public void Probe_EmitFailure_IsNotConstructible(bool legacyBroken, string reason)
    {
        CSharpCompilation good = Compile(Greeter, "Broken");
        CSharpCompilation broken = Compile(Greeter + "namespace N { public class Bad { public int M() => missing; } }", "Broken");
        CSharpCompilation legacy = legacyBroken ? broken : good;
        CSharpCompilation modern = legacyBroken ? good : broken;
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        ReplayPlan plan = factory.Probe(pair, [JsonDocument.Parse("\"Ada\"").RootElement], directory);

        Assert.Null(plan.Drivers);
        Assert.StartsWith(reason, plan.Reason, StringComparison.Ordinal);
        Assert.Contains("CS0103", plan.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "emit-failed: legacy project Broken: ")]
    [InlineData(false, "emit-failed: modern project Broken: ")]
    public void Plan_EmitFailure_IsNotConstructible(bool legacyBroken, string reason)
    {
        CSharpCompilation good = Compile(Greeter, "Broken");
        CSharpCompilation broken = Compile(Greeter + "namespace N { public class Bad { public int M() => missing; } }", "Broken");
        CSharpCompilation legacy = legacyBroken ? broken : good;
        CSharpCompilation modern = legacyBroken ? good : broken;
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(legacy, Method(legacy, "N.Greeter", "Greet"), GreetIr, modern, Method(modern, "N.Greeter", "Greet"), GreetIr);

        Assert.StartsWith(reason, factory.Plan(pair, candidate: null, directory).Reason, StringComparison.Ordinal);
    }
}
