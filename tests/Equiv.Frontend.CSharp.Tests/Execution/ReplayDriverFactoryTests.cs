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
    }
}
