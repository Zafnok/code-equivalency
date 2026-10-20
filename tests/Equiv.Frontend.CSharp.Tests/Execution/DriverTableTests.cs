using System.Reflection;
using System.Runtime.Loader;

using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-249: a replay's delegate argument answers from the model's table. The driver is compiled and its case run in
/// this process, so the table's answers are the ones a real driver gives: a recorded tuple's result, the default for a
/// tuple the table does not hold, <c>System.Exception</c> where the model's answer is that the call throws, and a
/// <c>void</c> delegate's call keyed by an object the case built.
/// </summary>
public sealed class DriverTableTests
{
    private const string Source = """
        namespace N
        {
            public class Box { }

            public static class U
            {
                public static int Use(System.Func<int, int> f, int x) => f(x);
                public static double Halve(System.Func<double, string, double> f, double x, string s) => f(x, s);
                public static int Visit(System.Action<Box> a, Box b) { a(b); return 1; }
                public static int Refs(ByRef r) => 0;
            }

            public delegate int ByRef(ref int x);
        }
        """;

    private static readonly CSharpCompilation Project = Compile(Source);

    [Theory]
    [InlineData("\"2\"", "[\"Returned\",5]")]
    [InlineData("\"4\"", "[\"Returned\",0]")]
    [InlineData("\"3\"", "[\"Threw\",\"System.Exception\"]")]
    public void ADelegate_AnswersFromTheModelsTable(string x, string outcome)
    {
        DriverHeap heap = new();
        INamedTypeSymbol func = (INamedTypeSymbol)Method(Project, "N.U", "Use").Parameters[0].Type;
        int maker = Assert.IsType<int>(heap.Table(func));
        string rows = $"[{DriverHeap.Row(["2"], "5", threw: false)},{DriverHeap.Row(["3"], "null", threw: true)}]";

        Assert.Equal(outcome, Run("Use", heap, $"[\"invariant\",{DriverHeap.Made("1", maker, ["0", rows])},{x}]"));
    }

    /// <summary>A <c>double</c> key compares by its bits, NaN to NaN, and a string key by its value.</summary>
    [Theory]
    [InlineData("\"0x7FF8000000000000\"", "\"a\"", "\"0x3FF0000000000000\"")]
    [InlineData("\"0x8000000000000000\"", "\"a\"", "\"0x4000000000000000\"")]
    [InlineData("\"0x0000000000000000\"", "\"a\"", "\"0x4008000000000000\"")]
    [InlineData("\"0x0000000000000000\"", "\"b\"", "\"0x0000000000000000\"")]
    public void ADelegatesKeys_CompareAsTheModelsValues(string x, string s, string result)
    {
        DriverHeap heap = new();
        INamedTypeSymbol func = (INamedTypeSymbol)Method(Project, "N.U", "Halve").Parameters[0].Type;
        int maker = Assert.IsType<int>(heap.Table(func));
        string rows = string.Join(
            ',',
            DriverHeap.Row(["\"0x7FF8000000000001\"", "\"a\""], "\"0x3FF0000000000000\"", threw: false),
            DriverHeap.Row(["\"0x8000000000000000\"", "\"a\""], "\"0x4000000000000000\"", threw: false),
            DriverHeap.Row(["\"0x0000000000000000\"", "\"a\""], "\"0x4008000000000000\"", threw: false));

        Assert.Equal(
            $"[\"Returned\",{result}]",
            Run("Halve", heap, $"[\"invariant\",{DriverHeap.Made("1", maker, ["\"0x0000000000000000\"", $"[{rows}]"])},{x},{s}]"));
    }

    /// <summary>An object key is the object the case built under that id; another object of its type is another tuple.</summary>
    [Theory]
    [InlineData("[\"@r\",\"2\"]", "[\"Threw\",\"System.Exception\"]")]
    [InlineData("[\"@c\",\"3\",1,[]]", "[\"Returned\",1]")]
    public void AVoidDelegate_IsKeyedByTheObjectItWasCalledWith(string b, string outcome)
    {
        DriverHeap heap = new();
        IMethodSymbol visit = Method(Project, "N.U", "Visit");
        int maker = Assert.IsType<int>(heap.Table((INamedTypeSymbol)visit.Parameters[0].Type));
        int box = Assert.IsType<int>(heap.Constructor(((INamedTypeSymbol)visit.Parameters[1].Type).InstanceConstructors.Single()));
        string a = DriverHeap.Made("1", maker, ["null", $"[{DriverHeap.Row([DriverHeap.Made("2", box, [])], "null", threw: true)}]"]);

        Assert.Equal(outcome, Run("Visit", heap, $"[\"invariant\",{a},{b}]"));
    }

    /// <summary>A delegate with a by-reference parameter has no table: a driver's table answers no <c>ref</c> or <c>out</c> output.</summary>
    [Fact]
    public void ADelegateWithAByReferenceParameter_HasNoTable()
    {
        Assert.Null(new DriverHeap().Table((INamedTypeSymbol)Method(Project, "N.U", "Refs").Parameters[0].Type));
    }

    /// <summary>The outcome line the generated driver's case gives for <paramref name="line"/>.</summary>
    private static string Run(string method, DriverHeap heap, string line)
    {
        string source = DriverSource.Generate(Method(Project, "N.U", method), new DriverSource.Shape(DriverSource.Receiver.New, DriverSource.Access.Direct, heap, Formatter: false) { Builds = true });
        CSharpCompilation driver = CSharpCompilation.Create(
            "EquivReplay",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Project.References, Project.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        AssemblyLoadContext context = new("driver-table", isCollectible: true);
        try
        {
            context.LoadFromStream(Emit(Project));
            Assembly loaded = context.LoadFromStream(Emit(driver));
            object parsed = loaded.GetType("J")!.GetMethod("Parse")!.Invoke(null, [line])!;
            return (string)loaded.GetType("Program")!.GetMethod("Case", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [parsed])!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static MemoryStream Emit(CSharpCompilation compilation)
    {
        MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        return image;
    }
}
