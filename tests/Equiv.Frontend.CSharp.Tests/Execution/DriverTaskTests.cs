using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-252 criterion 1: the source of a driver whose member returns a <c>Task</c>, a <c>Task&lt;T&gt;</c>, a
/// <c>ValueTask</c> or a <c>ValueTask&lt;T&gt;</c> waits for it and answers with its result, by each way a driver reaches
/// its member; each shape compiles against the project it calls.
/// </summary>
public sealed class DriverTaskTests
{
    private const string Source = """
        namespace N
        {
            using System.Threading.Tasks;

            public class T
            {
                public static Task<int> Twice(int x) => Task.FromResult(2 * x);
                public static Task Done() => Task.CompletedTask;
                public static ValueTask<int> Value(int x) => new ValueTask<int>(x);
                public static ValueTask Plain() => default;
                public static Task<object> Boxed() => Task.FromResult<object>(null);
                public static int Sync() => 0;
                private static Task<int> Hidden(Secret s) => Task.FromResult(1);
                private static ValueTask<int> HiddenValue(Secret s) => new ValueTask<int>(1);
                private static ValueTask HiddenPlain(Secret s) => default;
                private static Task<Secret> Made(Secret s) => Task.FromResult(s);
                private static Task<int> Delegated(int x) => Task.FromResult(x);
                private class Secret { }
            }
        }
        """;

    private static readonly CSharpCompilation Project = Compile(Source);

    private static IMethodSymbol Member(string name) => Method(Project, "N.T", name);

    /// <summary>The driver's source, which must compile against <see cref="Project"/> as the driver would.</summary>
    private static string Driver(string member, DriverSource.Access access)
    {
        string source = DriverSource.Generate(Member(member), new DriverSource.Shape(DriverSource.Receiver.New, access, new DriverHeap(), Formatter: false) { Builds = true });
        CSharpCompilation driver = CSharpCompilation.Create(
            "EquivReplay",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Project.References, Project.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        Diagnostic[] errors = [.. driver.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static e => e.ToString())));
        return source;
    }

    [Theory]
    [InlineData("Twice", "Direct", "int r;", "r = A.Get(global::N.T.Twice(p0));", "return Returned(W.I(r));")]
    [InlineData("Done", "Direct", "", "A.Wait(global::N.T.Done());", "return Returned(\"null\");")]
    [InlineData("Value", "Direct", "int r;", "r = A.Get((global::N.T.Value(p0)).AsTask());", "return Returned(W.I(r));")]
    [InlineData("Plain", "Direct", "", "A.Wait((global::N.T.Plain()).AsTask());", "return Returned(\"null\");")]
    [InlineData("Boxed", "Direct", "object r;", "r = A.Get(global::N.T.Boxed());", "return NotComparable(\"object\");")]
    [InlineData("Hidden", "Invoke", "int r;", "r = A.Get((global::System.Threading.Tasks.Task<int>)H.Call(m, null, new object[] { p0 }));", "return Returned(W.I(r));")]
    [InlineData("HiddenValue", "Invoke", "int r;", "r = A.Get(((global::System.Threading.Tasks.ValueTask<int>)H.Call(m, null, new object[] { p0 })).AsTask());", "return Returned(W.I(r));")]
    [InlineData("HiddenPlain", "Invoke", "", "A.Wait(((global::System.Threading.Tasks.ValueTask)H.Call(m, null, new object[] { p0 })).AsTask());", "return Returned(\"null\");")]
    [InlineData("Delegated", "Delegate", "int r;", "r = A.Get(f(p0));", "return Returned(W.I(r));")]
    public void AReturnedTask_IsWaitedForAndItsResultAnswered(string member, string access, string declaration, string call, string answer)
    {
        string source = Driver(member, Enum.Parse<DriverSource.Access>(access));

        Assert.Contains(declaration, source, StringComparison.Ordinal);
        Assert.Contains(call, source, StringComparison.Ordinal);
        Assert.Contains(answer, source, StringComparison.Ordinal);
        Assert.Contains("internal static class A", source, StringComparison.Ordinal);
    }

    /// <summary>A task whose result a driver cannot name is not waited for: it stays NotComparable as its own type.</summary>
    [Fact]
    public void ATaskOfAHiddenResult_IsNotComparable()
    {
        string source = Driver("Made", DriverSource.Access.Invoke);

        Assert.Contains("object r;", source, StringComparison.Ordinal);
        Assert.Contains("r = H.Call(m, null, new object[] { p0 });", source, StringComparison.Ordinal);
        Assert.Contains("return NotComparable(\"System.Threading.Tasks.Task<N.T.Secret>\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static class A", source, StringComparison.Ordinal);
    }

    /// <summary>A driver of a member that returns no task carries no task helper.</summary>
    [Fact]
    public void ADriverWithNoTask_HasNoTaskHelper() =>
        Assert.DoesNotContain("internal static class A", Driver("Sync", DriverSource.Access.Direct), StringComparison.Ordinal);
}
