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
/// Iterators stay whole-body opaque. Ticket P1-029: <c>await using</c> and <c>await foreach</c> lower as the control flow
/// graph desugars them, their <c>DisposeAsync()</c> and <c>MoveNextAsync()</c> calls each followed by an await of the result.
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
        Assert.False(call.Closed); // other code runs while the task is suspended (ADR 0041)
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

    /// <summary>Ticket P1-029, criterion 1: the disposal is <c>DisposeAsync()</c> on the resource and an await of its result.</summary>
    [Theory]
    [InlineData($"static async {Tasks}.Task M(IAsyncDisposable d) {{ await using (d) {{ }} }}")]
    [InlineData($"static async {Tasks}.Task M(IAsyncDisposable d) {{ await using IAsyncDisposable e = d; }}")]
    public void AwaitUsing_DisposesThroughAnAwaitedCall(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(2, Calls(procedure).Length);
        IrCall dispose = Calls(procedure)[0];
        IrCall awaited = Calls(procedure)[1];
        Assert.Equal("System.IAsyncDisposable::DisposeAsync()", dispose.Callee.Value);
        Assert.Equal("d", Assert.Single(dispose.Args).Name);
        Assert.Equal("await:System.Runtime.CompilerServices.ValueTaskAwaiter", awaited.Callee.Value);
        Assert.Equal(dispose.Target, Assert.Single(awaited.Args));
        Assert.Null(awaited.Target);
        Assert.NotNull(awaited.Threw);
    }

    /// <summary>Criterion 1: a body that throws disposes on that exit too, so the disposal and its await are there twice.</summary>
    [Fact]
    public void AwaitUsing_DisposesOnTheThrowingExit()
    {
        IrProcedure procedure = Method($"static void F() {{ }} static async {Tasks}.Task M(IAsyncDisposable d) {{ await using (d) {{ F(); }} }}");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(
            ["C::F()", "System.IAsyncDisposable::DisposeAsync()", "System.IAsyncDisposable::DisposeAsync()", "await:System.Runtime.CompilerServices.ValueTaskAwaiter", "await:System.Runtime.CompilerServices.ValueTaskAwaiter"],
            Calls(procedure).Select(static c => c.Callee.Value).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        IrCall body = Assert.Single(Calls(procedure), static c => Is(c, "C::F()"));
        IrBlock thrown = procedure.Blocks.Single(b => b.Instructions.Contains(body));
        IrBlockId exceptional = ((IrBranch)thrown.Terminator).Then;
        Assert.Contains(Reachable(procedure, exceptional), static b => b.Terminator is IrThrow { ExceptionType: "System.Exception" });
        Assert.Contains(Reachable(procedure, exceptional).SelectMany(static b => b.Instructions).OfType<IrCall>(), static c => c.Callee.Value.StartsWith("await:", StringComparison.Ordinal));
        Assert.DoesNotContain(Reachable(procedure, exceptional), static b => b.Terminator is IrReturn);
    }

    /// <summary>Criterion 1: a null resource of a reference type is not disposed, and nothing is awaited.</summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public void AwaitUsing_SkipsANullResource(bool isNull, int calls)
    {
        IrProcedure procedure = Method($"static async {Tasks}.Task<int> M(IAsyncDisposable d) {{ await using (d) {{ }} return 1; }}");

        IrRun run = IrInterpreter.Run(
            procedure, new IrInputs([Reference(0, "System.IAsyncDisposable"), Nulls("System.IAsyncDisposable", 0, isNull)]), new Completes(), stepBudget: 100);

        Assert.Equal(new IrReturned(Bits(32, 1)), run.Outcome);
        Assert.Equal(calls, run.Trace.Length);
    }

    /// <summary>A struct resource has no null to skip: it is disposed unconditionally.</summary>
    [Fact]
    public void AwaitUsing_AStructResourceIsDisposedWithoutANullTest()
    {
        IrProcedure procedure = Source($$"""
            using System;
            struct S : IAsyncDisposable { public {{Tasks}}.ValueTask DisposeAsync() => default; }
            class C { static async {{Tasks}}.Task M(S s) { await using (s) { } } }
            """);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(2, Calls(procedure).Length);
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("null.", StringComparison.Ordinal));
    }

    /// <summary>Several resources in one statement are nested by the control flow graph: the last acquired is disposed first.</summary>
    [Fact]
    public void AwaitUsing_SeveralResourcesAreDisposedInReverseOrder()
    {
        IrProcedure procedure = Method(
            $"static async {Tasks}.Task<int> M(System.IO.Stream a, System.IO.TextWriter b) {{ await using (System.IO.Stream x = a) await using (System.IO.TextWriter y = b) {{ }} return 1; }}");

        IrRun run = IrInterpreter.Run(
            procedure,
            new IrInputs([Reference(0, "System.IO.Stream"), Reference(0, "System.IO.TextWriter"), .. procedure.Parameters[2..].Select(static p => Nulls(((IrSort)((IrMap)p.Var.Type).Key).Name, 0, isNull: false))]),
            new Completes(),
            stepBudget: 100);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(
            ["System.IO.TextWriter::DisposeAsync()", "await:System.Runtime.CompilerServices.ValueTaskAwaiter", "System.IO.Stream::DisposeAsync()", "await:System.Runtime.CompilerServices.ValueTaskAwaiter"],
            run.Trace.Select(static c => c.Callee.Value),
            StringComparer.Ordinal);
    }

    /// <summary>A pattern-based <c>DisposeAsync</c> whose result has no instance <c>GetAwaiter</c> leaves only that await opaque.</summary>
    [Fact]
    public void AwaitUsing_AnAwaiterFoundOnlyByExtensionIsOpaque()
    {
        IrProcedure procedure = Source($$"""
            using System;
            class Pending { }
            static class E { public static System.Runtime.CompilerServices.TaskAwaiter GetAwaiter(this Pending p) => {{Tasks}}.Task.CompletedTask.GetAwaiter(); }
            class R { public Pending DisposeAsync() => new Pending(); }
            class C { static async {{Tasks}}.Task M(R r) { await using (r) { } } }
            """);

        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal("Await", opaque.Reason);
        Assert.False(opaque.WholeBody);
        Assert.Empty(Calls(procedure)); // the opaque stands for the whole await, its operand included
    }

    /// <summary>Criterion 2: the enumerator loop, each <c>MoveNextAsync()</c> and the <c>DisposeAsync()</c> in the <c>finally</c> awaited.</summary>
    [Fact]
    public void AwaitForeach_AwaitsMoveNextAndDisposes()
    {
        IrProcedure procedure = Method(
            $"static async {Tasks}.Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs) {{ int s = 0; await foreach (int x in xs) s += x; return s; }}");

        Assert.Empty(Opaques(procedure));
        ImmutableArray<IrCall> calls = Calls(procedure);
        IrCall enumerator = Assert.Single(calls, static c => Is(c, "System.Collections.Generic.IAsyncEnumerable`1::GetAsyncEnumerator(System.Threading.CancellationToken)<int>"));
        IrCall moveNext = Assert.Single(calls, static c => Is(c, "System.Collections.Generic.IAsyncEnumerator`1::MoveNextAsync()<int>"));
        IrCall moved = Assert.Single(calls, static c => Is(c, "await:System.Runtime.CompilerServices.ValueTaskAwaiter`1<bool>"));
        IrCall current = Assert.Single(calls, static c => Is(c, "System.Collections.Generic.IAsyncEnumerator`1::get_Current()<int>"));
        Assert.Equal(enumerator.Target, Assert.Single(moveNext.Args));
        Assert.Equal(moveNext.Target, Assert.Single(moved.Args));
        Assert.Equal(new IrBool(), moved.Target!.Type);
        Assert.Equal(enumerator.Target, Assert.Single(current.Args));
        // The token the compiler passes for the optional parameter is its type's default, a constant.
        IrConst token = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>(), c => c.Target == enumerator.Args[1]);
        Assert.Equal(new IrSortValue("System.Threading.CancellationToken", 0), token.Value);
        // One disposal per exit of the `try`: the loop's end, and a throw from `MoveNextAsync`, its await or `Current`.
        IrCall[] disposals = [.. calls.Where(static c => Is(c, "System.IAsyncDisposable::DisposeAsync()"))];
        Assert.NotEmpty(disposals);
        Assert.All(disposals, d =>
        {
            Assert.Equal(enumerator.Target, Assert.Single(d.Args));
            Assert.Contains(calls, c => Is(c, "await:System.Runtime.CompilerServices.ValueTaskAwaiter") && c.Args.Single() == d.Target);
        });
    }

    /// <summary>Criterion 2: <c>WithCancellation</c> and <c>ConfigureAwait</c> are calls on the collection, and their result is enumerated.</summary>
    [Fact]
    public void AwaitForeach_WithCancellationIsACallOnTheCollection()
    {
        IrProcedure procedure = Source(
            $"using {Tasks};\nclass C {{ static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs, System.Threading.CancellationToken ct) {{ int s = 0; await foreach (int x in xs.WithCancellation(ct).ConfigureAwait(false)) s += x; return s; }} }}");

        Assert.Empty(Opaques(procedure));
        ImmutableArray<IrCall> calls = Calls(procedure);
        IrCall with = Assert.Single(calls, static c => Is(c, "System.Threading.Tasks.TaskAsyncEnumerableExtensions::WithCancellation`1(System.Collections.Generic.IAsyncEnumerable<int>,System.Threading.CancellationToken)<int>"));
        IrCall configured = Assert.Single(calls, static c => Is(c, "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1::ConfigureAwait(bool)<int>"));
        IrCall enumerator = Assert.Single(calls, static c => Is(c, "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1::GetAsyncEnumerator()<int>"));
        IrCall moveNext = Assert.Single(calls, static c => Is(c, "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1.Enumerator::MoveNextAsync()<int>"));
        Assert.Equal(["xs", "ct"], with.Args.Select(static a => a.Name), StringComparer.Ordinal);
        Assert.Equal(with.Target, configured.Args[0]);
        Assert.Equal(configured.Target, Assert.Single(enumerator.Args));
        Assert.Equal(enumerator.Target, Assert.Single(moveNext.Args));
        Assert.Contains(calls, static c => Is(c, "await:System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1.ConfiguredValueTaskAwaiter<bool>"));
        Assert.Contains(calls, static c => Is(c, "await:System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter"));
    }

    private static bool Is(IrCall call, string identity) => string.Equals(call.Callee.Value, identity, StringComparison.Ordinal);

    /// <summary>The blocks reachable from <paramref name="start"/>, itself included.</summary>
    private static ImmutableArray<IrBlock> Reachable(IrProcedure procedure, IrBlockId start)
    {
        Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        HashSet<IrBlockId> seen = [start];
        Queue<IrBlockId> pending = new([start]);
        while (pending.TryDequeue(out IrBlockId? id))
        {
            IEnumerable<IrBlockId> next = blocks[id].Terminator switch
            {
                IrBranch branch => [branch.Then, branch.Else],
                IrGoto jump => [jump.Target],
                _ => [],
            };
            foreach (IrBlockId successor in next.Where(seen.Add))
            {
                pending.Enqueue(successor);
            }
        }

        return [.. seen.Select(id => blocks[id])];
    }

    /// <summary>Every call completes: it does not throw, and yields the default of its result type.</summary>
    private sealed class Completes : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts) =>
            new(resultType is IrSort sort ? new IrSortValue(sort.Name, 0) : null, Threw: false) { RefOuts = [] };
    }

    /// <summary>The text of a one-line <paramref name="members"/> that <paramref name="span"/> covers.</summary>
    private static string Spanned(string members, SourceSpan span) => members[(span.StartColumn - 1)..(span.EndColumn - 1)];
}
