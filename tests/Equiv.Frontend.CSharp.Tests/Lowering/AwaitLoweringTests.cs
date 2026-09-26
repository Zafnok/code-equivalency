using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket M4-006: an <c>async</c> method is its synchronous body, returning the task's result, and <c>await e</c> is an
/// <see cref="IrCall"/> <c>await:&lt;awaiter type&gt;</c> of the awaitable, yielding the awaited value, with a <c>threw</c> edge.
/// Iterators, <c>await foreach</c> and <c>await using</c> stay whole-body opaque.
/// </summary>
public sealed class AwaitLoweringTests
{
    private const string Tasks = "System.Threading.Tasks";

    [Fact]
    public void AwaitIsACallOnTheAwaitable()
    {
        IrProcedure procedure = Method($"static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ int x = await t; return x + 1; }}");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrBitVec(32), procedure.ReturnType);
        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("await:System.Runtime.CompilerServices.TaskAwaiter`1<int>", call.Callee.Value);
        Assert.Equal("t", Assert.Single(call.Args).Name);
        Assert.Equal(new IrBitVec(32), call.Target!.Type);
        Assert.NotNull(call.Threw);
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.Exception" });
    }

    [Fact]
    public void AReferenceAwaitableIsNullCheckedBeforeTheAwait()
    {
        IrProcedure procedure = Method($"static async {Tasks}.Task M({Tasks}.Task t) {{ await t; }}");

        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("await:System.Runtime.CompilerServices.TaskAwaiter", call.Callee.Value);
        Assert.Null(call.Target);
    }

    [Fact]
    public void AValueTaskAwaitableIsNotNullChecked()
    {
        IrProcedure procedure = Method($"static async {Tasks}.ValueTask<int> M({Tasks}.ValueTask<int> t) => await t;");

        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Equal("await:System.Runtime.CompilerServices.ValueTaskAwaiter`1<int>", Assert.Single(Calls(procedure)).Callee.Value);
        Assert.Equal(new IrBitVec(32), procedure.ReturnType);
    }

    [Fact]
    public void AnExtensionGetAwaiterIsNotNullChecked()
    {
        IrProcedure procedure = Source($$"""
            using System;
            static class E { public static System.Runtime.CompilerServices.TaskAwaiter GetAwaiter(this string s) => {{Tasks}}.Task.CompletedTask.GetAwaiter(); }
            class C { static async {{Tasks}}.Task M(string s) { await s; } }
            """);

        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Equal("await:System.Runtime.CompilerServices.TaskAwaiter", Assert.Single(Calls(procedure)).Callee.Value);
    }

    [Theory]
    [InlineData($"static async void M({Tasks}.Task t) {{ await t; }}")]
    [InlineData($"static async {Tasks}.ValueTask M({Tasks}.Task t) {{ await t; }}")]
    public void AnAsyncMethodWithoutAResultReturnsNothing(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Null(procedure.ReturnType);
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void TwoAwaitsOfTheSameTaskAreTwoCalls()
    {
        IrProcedure procedure = Method($"static async {Tasks}.Task<int> M({Tasks}.Task<int> t) => await t - await t;");

        Assert.Equal(2, Calls(procedure).Length);
        Assert.All(Calls(procedure), static c => Assert.Equal("t", Assert.Single(c.Args).Name));
    }

    [Fact]
    public void TheAwaiterTypeIsRenamed()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("System.Runtime.CompilerServices", "Awaiters"), []);

        IrProcedure procedure = Source(
            $"using System;\nclass C {{ static async {Tasks}.Task M({Tasks}.Task t) {{ await t; }} }}", renames: renames);

        Assert.Equal("await:Awaiters.TaskAwaiter", Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>Acceptance criterion 3: <c>ConfigureAwait(false)</c> is a call whose result is what is awaited, and nothing more.</summary>
    [Fact]
    public void ConfigureAwaitIsAnOrdinaryCall()
    {
        IrProcedure procedure = Method($"static async {Tasks}.Task<int> M({Tasks}.Task<int> t) => await t.ConfigureAwait(false);");

        Assert.Empty(Opaques(procedure));
        IrCall configure = Calls(procedure)[0];
        IrCall awaited = Calls(procedure)[1];
        Assert.StartsWith("System.Threading.Tasks.Task`1::ConfigureAwait(bool)", configure.Callee.Value, StringComparison.Ordinal);
        Assert.Equal("await:System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1.ConfiguredTaskAwaiter<int>", awaited.Callee.Value);
        Assert.Equal(configure.Target, Assert.Single(awaited.Args));
    }

    /// <summary>Acceptance criterion 4: each keeps its own reason, and its span is the first offending construct.</summary>
    [Theory]
    [InlineData("static System.Collections.Generic.IEnumerable<int> M() { yield return 1; }", "iterator", "yield return 1;")]
    [InlineData("static System.Collections.Generic.IEnumerable<int> M(bool b) { if (b) yield break; yield return 2; }", "iterator", "yield break;")]
    [InlineData($"static async System.Collections.Generic.IAsyncEnumerable<int> M({Tasks}.Task t) {{ await t; yield return 1; }}", "iterator", "yield return 1;")]
    [InlineData($"static async {Tasks}.Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs) {{ int s = 0; await foreach (int x in xs) s += x; return s; }}", "await-foreach", "await foreach (int x in xs) s += x;")]
    [InlineData($"static async {Tasks}.Task M(IAsyncDisposable d) {{ await using (d) {{ }} }}", "await-using", "await using (d) { }")]
    [InlineData($"static async {Tasks}.Task M(IAsyncDisposable d) {{ await using IAsyncDisposable e = d; }}", "await-using", "await using IAsyncDisposable e = d;")]
    public void IteratorStaysOpaque(string members, string reason, string construct)
    {
        IrProcedure procedure = Method(members);

        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal(reason, opaque.Reason);
        Assert.True(opaque.WholeBody);
        Assert.Equal((4, 4), (opaque.Span.StartLine, opaque.Span.EndLine));
        Assert.Equal(construct, Spanned(members, opaque.Span));
    }

    [Theory]
    [InlineData("static async System.Threading.Tasks.Task M(dynamic d) { await d; }")]
    [InlineData("""
        interface IAwaiter : System.Runtime.CompilerServices.INotifyCompletion { bool IsCompleted { get; } int GetResult(); }
        class Awaitable<T> where T : IAwaiter { public T GetAwaiter() => default(T); }
        static async System.Threading.Tasks.Task<int> M<T>(Awaitable<T> a) where T : IAwaiter => await a;
        """)]
    public void AnAwaiterThatIsNotANamedTypeIsOpaque(string members)
    {
        IrOpaque opaque = Assert.Single(Opaques(Method(members)));

        Assert.Equal("Await", opaque.Reason);
        Assert.False(opaque.WholeBody);
    }

    [Fact]
    public void ASynchronousForEachOrUsingInAnAsyncMethodIsLowered()
    {
        IrProcedure procedure = Method(
            $"static async {Tasks}.Task<int> M(System.Collections.Generic.List<int> xs, IDisposable d) {{ int s = 0; using (d) {{ foreach (int x in xs) s += x; }} using IDisposable e = d; await {Tasks}.Task.Yield(); return s; }}");

        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void AnIteratorsResultIsItsEnumerable() =>
        Assert.Equal(
            new IrSort("System.Collections.Generic.IEnumerable`1"),
            Method("static System.Collections.Generic.IEnumerable<int> M() { yield return 1; }").ReturnType);

    [Fact]
    public void AnAsyncLambdaInASyncMethodDoesNotMakeItWholeBodyOpaque()
    {
        IrProcedure procedure = Method(
            $"static int M(System.Collections.Generic.IAsyncEnumerable<int> xs) {{ Func<{Tasks}.Task> f = async () => {{ await foreach (int x in xs) {{ }} }}; return 1; }}");

        Assert.All(Opaques(procedure), static o => Assert.False(o.WholeBody));
    }

    /// <summary>The text of a one-line <paramref name="members"/> that <paramref name="span"/> covers.</summary>
    private static string Spanned(string members, SourceSpan span) => members[(span.StartColumn - 1)..(span.EndColumn - 1)];
}
