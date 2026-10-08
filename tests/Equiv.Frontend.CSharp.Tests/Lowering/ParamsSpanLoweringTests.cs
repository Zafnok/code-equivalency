using System.Collections.Immutable;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-143: the span the compiler builds from elements for a <c>params ReadOnlySpan&lt;T&gt;</c> parameter is a new
/// array of the elements read through the <c>cast</c> map of the array to the span, and an API-equivalence adapter's
/// <c>rest</c> item passes a legacy call's <c>params</c> elements on as that span, so the rewritten legacy call and the
/// modern call are the same IR. The test compilation references .NET 10, where the source already binds the span
/// overloads, so the legacy members are stand-ins named by the entries' legacy identities.
/// </summary>
public sealed class ParamsSpanLoweringTests
{
    /// <summary>Stand-ins for the <c>params</c> array overloads, which are all a framework before .NET 9 has.</summary>
    private const string Old = """
        using System;
        using System.Text;
        static class Old
        {
            public static string Format(IFormatProvider provider, string format, params object[] args) => null!;
            public static string Join(char separator, params string[] value) => null!;
            public static string Combine(params string[] paths) => null!;
            public static string Plain(string path) => null!;
            public static string Sum(params int[][] rows) => null!;
        }
        class Builder { public StringBuilder AppendFormat(IFormatProvider provider, string format, params object[] args) => null!; }

        """;

    private const string Usings = "using System;\nusing System.IO;\nusing System.Text;\n";

    private static ApiEquivalence Entry(string id) => ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>The catalogue's four entries, each with its legacy member the stand-in's.</summary>
    private static ImmutableArray<ApiEquivalence> Spans =>
    [
        Entry("bcl.string-format-provider-params-span") with { Legacy = "Old::Format(System.IFormatProvider,string,object[])" },
        Entry("bcl.string-join-char-params-span") with { Legacy = "Old::Join(char,string[])" },
        Entry("bcl.string-builder-append-format-provider-params-span") with { Legacy = "Builder::AppendFormat(System.IFormatProvider,string,object[])" },
        Entry("bcl.path-combine-params-span") with { Legacy = "Old::Combine(string[])" },
    ];

    private static IrProcedure Modern(string method) => Source($"{Usings}class C {{ {method} }}");

    private static (IrProcedure Body, ImmutableArray<string> Applied) Rewritten(string method) => Legacy($"{Old}class C {{ {method} }}", Spans);

    /// <summary>Criterion 1: the span of a <c>params</c> span parameter lowers with no opaque node, as its array through the cast.</summary>
    [Fact]
    public void TheSpanOfAParamsSpanParameterIsItsArrayThroughTheCast()
    {
        IrProcedure procedure = Modern("static string M(IFormatProvider p, string f, object a, object b, object c, object d) => string.Format(p, f, a, b, c, d);");

        Assert.Empty(Opaques(procedure));
        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("System.String::Format(System.IFormatProvider,string,System.ReadOnlySpan<object>)", call.Callee.Value);
        IrMapRead span = procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>().Single(r => r.Target == call.Args[2]);
        Assert.Equal("cast.object__.System.ReadOnlySpan_1", span.Map.Name);
        Assert.Equal(new IrSort("System.ReadOnlySpan`1"), span.Target.Type);
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "new.object__", StringComparison.Ordinal));
    }

    /// <summary>The elements are stored in order, so another order is another body.</summary>
    [Fact]
    public void AnotherElementOrderIsAnotherBody() =>
        Assert.NotEqual(
            IrText.Dump(Modern("static string M(char s, string a, string b) => string.Join(s, a, b);")),
            IrText.Dump(Modern("static string M(char s, string a, string b) => string.Join(s, b, a);")),
            StringComparer.Ordinal);

    /// <summary>
    /// A <c>ReadOnlySpan&lt;T&gt;</c> collection expression written in the source is the same array through the same cast,
    /// and an empty one is an array of length 0: the compiler makes no <c>Array.Empty</c> call for a span.
    /// </summary>
    [Theory]
    [InlineData("static int M(int a, int b) { ReadOnlySpan<int> s = [a, b]; return s.Length; }")]
    [InlineData("static int M() { ReadOnlySpan<int> s = []; return s.Length; }")]
    [InlineData("static string M() => Path.Combine();")]
    public void AReadOnlySpanCollectionExpressionIsItsArrayThroughTheCast(string method)
    {
        IrProcedure procedure = Modern(method);

        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(Calls(procedure), static c => c.Callee.Value.StartsWith("System.Array::Empty", StringComparison.Ordinal));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name.StartsWith("cast.", StringComparison.Ordinal) && p.Var.Name.EndsWith(".System.ReadOnlySpan_1", StringComparison.Ordinal));
    }

    /// <summary>A span of elements the array creation would not create stays opaque, as the array does.</summary>
    [Fact]
    public void ASpanOfArraysStaysOpaque() =>
        Assert.Equal(
            "CollectionExpression",
            Assert.Single(Opaques(Modern("static int M(int[] a) { ReadOnlySpan<int[]> s = [a]; return s.Length; }"))).Reason);

    /// <summary>
    /// Criteria 2 and 3: each entry rewrites the legacy call that passes elements to the modern member, and the result is
    /// the IR of the modern call with the same elements, the empty list and elements that need a conversion included.
    /// </summary>
    [Theory]
    [InlineData(
        "bcl.string-format-provider-params-span",
        "static string M(IFormatProvider p, string f, object a, object b, object c, object d) => Old.Format(p, f, a, b, c, d);",
        "static string M(IFormatProvider p, string f, object a, object b, object c, object d) => string.Format(p, f, a, b, c, d);")]
    [InlineData(
        "bcl.string-format-provider-params-span",
        "static string M(IFormatProvider p, string f, int a, string b, object c, long d) => Old.Format(p, f, a, b, c, d);",
        "static string M(IFormatProvider p, string f, int a, string b, object c, long d) => string.Format(p, f, a, b, c, d);")]
    [InlineData(
        "bcl.string-join-char-params-span",
        "static string M(char s, string a, string b) => Old.Join(s, a, b);",
        "static string M(char s, string a, string b) => string.Join(s, a, b);")]
    [InlineData(
        "bcl.path-combine-params-span",
        "static string M() => Old.Combine();",
        "static string M() => Path.Combine();")]
    [InlineData(
        "bcl.path-combine-params-span",
        "static string M(string a, string b, string c, string d, string e) => Old.Combine(a, b, c, d, e);",
        "static string M(string a, string b, string c, string d, string e) => Path.Combine(a, b, c, d, e);")]
    public void ALegacyCallThatPassesElementsIsTheModernCallOfTheirSpan(string id, string legacy, string modern)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(legacy);

        Assert.Equal([id], applied);
        Assert.Empty(Opaques(body));
        Assert.Equal(IrText.Dump(Modern(modern)), IrText.Dump(body));
    }

    /// <summary>The instance entry passes the receiver and the two arguments before the elements, and keeps the receiver's null check.</summary>
    [Fact]
    public void LegacyAppendFormat_IsTheModernCallOfTheSpanAndKeepsTheReceiverNullCheck()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(
            "static StringBuilder M(Builder t, IFormatProvider p, string f, object a, object b, object c, object d) => t.AppendFormat(p, f, a, b, c, d);");

        Assert.Equal(["bcl.string-builder-append-format-provider-params-span"], applied);
        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.Text.StringBuilder::AppendFormat(System.IFormatProvider,string,System.ReadOnlySpan<object>)", call.Callee.Value);
        Assert.Equal(["t", "p", "f"], call.Args[..3].Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.Equal(new IrSort("System.ReadOnlySpan`1"), call.Args[3].Type);
        IrBranch check = Assert.Single(body.Blocks.Select(static b => b.Terminator).OfType<IrBranch>(), static b => string.Equals(b.Cond.SourceName, "t", StringComparison.Ordinal));
        Assert.Equal("System.NullReferenceException", Assert.IsType<IrThrow>(body.Blocks.Single(b => b.Id == check.Then).Terminator).ExceptionType);
    }

    /// <summary>
    /// An element the control flow graph evaluates ahead of the call is stored where the modern side stores it: the two
    /// bodies make the same calls and store the same values in the same order, the captured one first. They differ only in
    /// a capture of the array's length that the legacy graph makes and nothing reads.
    /// </summary>
    [Fact]
    public void AnElementEvaluatedAheadOfTheCallIsStoredInOrder()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten("static string M(char s, string a, string b) => Old.Join(s, a ?? b, b);");
        IrProcedure modern = Modern("static string M(char s, string a, string b) => string.Join(s, a ?? b, b);");

        Assert.Equal(["bcl.string-join-char-params-span"], applied);
        Assert.Empty(Opaques(body));
        Assert.Equal(Calls(modern).Select(static c => c.Callee.Value), Calls(body).Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal([true, false], Stored(body));
        Assert.Equal(Stored(modern), Stored(body));
    }

    /// <summary>For each value stored into an element of a string array, in order, whether it is a flow capture's and not a parameter's.</summary>
    private static ImmutableArray<bool> Stored(IrProcedure procedure) =>
    [
        .. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>()
            .Where(static w => w.Map.Type is IrMap { Key: IrBitVec, Value: IrSort })
            .Select(static w => string.IsNullOrEmpty(w.Value.SourceName)),
    ];

    /// <summary>The modern AppendFormat call makes the same call of the same span.</summary>
    [Fact]
    public void ModernAppendFormat_BindsTheSpanOverloadTheEntryNames()
    {
        IrProcedure procedure = Modern(
            "static StringBuilder M(StringBuilder t, IFormatProvider p, string f, object a, object b, object c, object d) => t.AppendFormat(p, f, a, b, c, d);");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(Entry("bcl.string-builder-append-format-provider-params-span").Modern, Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>Criterion 4: the modern call with two elements swapped is not the rewritten legacy call.</summary>
    [Fact]
    public void SwappedElementsOnTheModernSideAreAnotherBody() =>
        Assert.NotEqual(
            IrText.Dump(Modern("static string M(char s, string a, string b) => string.Join(s, b, a);")),
            IrText.Dump(Rewritten("static string M(char s, string a, string b) => Old.Join(s, a, b);").Body),
            StringComparer.Ordinal);

    /// <summary>
    /// Criterion 4: a call that passes an array, not elements, keeps its legacy callee, and so does one whose
    /// <c>params</c> array the array creation would not create.
    /// </summary>
    [Theory]
    [InlineData("static string M(IFormatProvider p, string f, object[] a) => Old.Format(p, f, a);", "Old::Format(System.IFormatProvider,string,object[])")]
    [InlineData("static string M(IFormatProvider p, string f, object a) => Old.Format(p, f, new object[] { a });", "Old::Format(System.IFormatProvider,string,object[])")]
    [InlineData("static string M(char s, string[] a) => Old.Join(s, a);", "Old::Join(char,string[])")]
    [InlineData("static string M(string[] a) => Old.Combine(a);", "Old::Combine(string[])")]
    [InlineData("static string M(int[] a) => Old.Sum(a, a);", "Old::Sum(int[][])")]
    public void ACallThatPassesAnArrayIsNotRewritten(string method, string callee)
    {
        ImmutableArray<ApiEquivalence> entries = [.. Spans, Spans[3] with { Id = "t.sum", Legacy = "Old::Sum(int[][])" }];

        (IrProcedure body, ImmutableArray<string> applied) = Legacy($"{Old}class C {{ {method} }}", entries);

        Assert.Equal(callee, Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    /// <summary>
    /// The span overloads are .NET 9's, so the entries apply only to a pair that crosses it: on any other pair both sides
    /// bind the array overload, and the legacy call keeps its callee.
    /// </summary>
    [Theory]
    [InlineData("net48", "net8.0", "Old::Join(char,string[])", 0)]
    [InlineData("net9.0", "net10.0", "Old::Join(char,string[])", 0)]
    [InlineData("net8.0", "net9.0", "System.String::Join(char,System.ReadOnlySpan<string>)", 1)]
    [InlineData("net8.0", "net10.0", "System.String::Join(char,System.ReadOnlySpan<string>)", 1)]
    public void TheEntriesApplyOnlyToAPairThatCrossesTheRuntimeThatAddedTheSpanOverloads(string legacy, string modern, string callee, int entries)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"{Old}class C {{ static string M(char s, string a, string b) => Old.Join(s, a, b); }}");
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        (IrProcedure body, ImmutableArray<string> applied) = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Spans, Runtimes.Between(legacy, modern));

        Assert.Equal(callee, Assert.Single(Calls(body)).Callee.Value);
        Assert.Equal(entries, applied.Length);
    }

    /// <summary>
    /// A <c>rest</c> item that does not start at the first element of the call's <c>params</c> array, or whose legacy
    /// member has none, does not address the call.
    /// </summary>
    [Theory]
    [InlineData("Old::Join(char,string[])", "static string M(char s, string a, string b) => Old.Join(s, a, b);", 2)]
    [InlineData("Old::Join(char,string[])", "static string M(char s, string a, string b) => Old.Join(s, a, b);", 0)]
    [InlineData("Old::Plain(string)", "static string M(string a) => Old.Plain(a);", 0)]
    [InlineData("Old::Plain(string)", "static string M(string a) => Old.Plain(a);", 1)]
    public void ARestItemThatIsNotTheParamsElements_LeavesTheCallAsItIs(string member, string method, int rest)
    {
        ApiEquivalence entry = Spans[3] with { Legacy = member, Arguments = [.. Enumerable.Range(0, rest).Select(static i => new ApiArgument(i)), new ApiArgument(rest, Rest: true)] };

        (IrProcedure body, ImmutableArray<string> applied) = Legacy($"{Old}class C {{ {method} }}", [entry]);

        Assert.Equal(member, Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    /// <summary>
    /// The elements are evaluated where the source has them: a named <c>params</c> argument ahead of the others is
    /// evaluated, and its span built, before them.
    /// </summary>
    [Fact]
    public void ElementsNamedAheadOfTheOtherArgumentsAreEvaluatedFirst()
    {
        const string Members = "static string A() => null!; static char S() => ','; ";

        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(Members + "static string M() => Old.Join(value: A(), separator: S());");

        Assert.Equal(["bcl.string-join-char-params-span"], applied);
        Assert.Equal(["C::A()", "C::S()", "System.String::Join(char,System.ReadOnlySpan<string>)"], Calls(body).Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal(
            IrText.Dump(Modern(Members + "static string M() => string.Join(value: A(), separator: S());")),
            IrText.Dump(body));
    }
}
