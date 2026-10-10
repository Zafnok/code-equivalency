using System.Collections.Immutable;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-117: a call of <c>Enum.IsDefined</c> or <c>Enum.GetValues</c> that passes <c>typeof</c> of an enum is the
/// call of the generic member constructed with that enum, so the rewritten legacy body and the modern body are the same
/// IR; a <c>System.Type</c> argument that is anything else leaves the call as it is.
/// </summary>
public sealed class GenericEnumMembersLoweringTests
{
    private const string Usings = "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing System.Linq;\n";

    private const string IsDefined = "bcl.enum-is-defined-generic";

    private const string GetValues = "bcl.enum-get-values-generic";

    /// <summary>A member no entry of the shipped table names, an entry for it that passes nothing, and what the loops below enumerate.</summary>
    private const string Old = """
        static class Old
        {
            public static int[] Make() => null!;
            public static int[] Made() => null!;
            public static IEnumerable<int> Wrap(object o) => null!;
        }

        """;

    private static readonly ApiEquivalence Made = new("test.made", IsType: false, "Old::Make()", "Old::Made()", [], "r", new Uri("https://learn.microsoft.com/x"));

    private static ImmutableArray<ApiEquivalence> Entries => [.. ApiEquivalenceTable.Load().Entries.Where(static e => e.Id is IsDefined or GetValues), Made];

    private static (IrProcedure Body, ImmutableArray<string> Applied) Rewritten(string method) => Legacy($"{Usings}{Old}class C {{ {method} }}", Entries);

    private static IrProcedure Modern(string method) => Source($"{Usings}{Old}class C {{ {method} }}");

    /// <summary>Criterion 1: each <c>Type</c>-taking call with a <c>typeof</c> of an enum is the IR of the generic call that replaces it.</summary>
    [Theory]
    [InlineData(
        IsDefined,
        "static bool M(DayOfWeek d) => Enum.IsDefined(typeof(DayOfWeek), d);",
        "static bool M(DayOfWeek d) => Enum.IsDefined(d);")]
    [InlineData(
        GetValues,
        "static Array M() => Enum.GetValues(typeof(DayOfWeek));",
        "static Array M() => Enum.GetValues<DayOfWeek>();")]
    [InlineData(
        GetValues,
        "static int M() { int n = 0; foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>()) { n++; } return n; }",
        "static int M() { int n = 0; foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>()) { n++; } return n; }")]
    public void ATypeTakingCallWithATypeOfIsTheGenericCall(string id, string legacy, string modern)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(legacy);

        Assert.Equal([id], applied);
        Assert.Empty(Opaques(body));
        Assert.Equal(IrText.Dump(Modern(modern)), IrText.Dump(body));
    }

    /// <summary>The generic member is named with the enum the <c>typeof</c> names, and the <c>typeof</c> itself is not evaluated.</summary>
    [Fact]
    public void TheModernMemberIsConstructedWithTheEnum()
    {
        IrProcedure body = Rewritten("static bool M(ConsoleColor c) => Enum.IsDefined(typeof(ConsoleColor), c);").Body;

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.Enum::IsDefined`1(System.ConsoleColor)<System.ConsoleColor>", call.Callee.Value);
        Assert.Equal(["c"], call.Args.Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.DoesNotContain(body.Parameters, static p => p.Var.Name.StartsWith("typeof.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Criterion 2: a <c>Type</c> argument that is not a <c>typeof</c> of the value's own enum does not use the
    /// equivalence. The call stays the <c>Type</c>-taking one, and no entry is applied.
    /// </summary>
    [Theory]
    [InlineData("static bool M(Type t, DayOfWeek d) => Enum.IsDefined(t, d);")]
    [InlineData("static bool M(DayOfWeek d) => Enum.IsDefined(typeof(ConsoleColor), d);")]
    [InlineData("static bool M(int d) => Enum.IsDefined(typeof(DayOfWeek), d);")]
    [InlineData("static bool M(int d) => Enum.IsDefined(typeof(int), d);")]
    [InlineData("static bool M(object d) => Enum.IsDefined(typeof(DayOfWeek), d);")]
    [InlineData("static Array M(Type t) => Enum.GetValues(t);")]
    [InlineData("static Array M() => Enum.GetValues(typeof(int));")]
    public void ATypeThatIsNotATypeOfOfTheSameEnumIsNotRewritten(string method)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(method);

        Assert.Empty(applied);
        Assert.StartsWith("System.Enum::", Assert.Single(Calls(body)).Callee.Value, StringComparison.Ordinal);
        Assert.Contains("(System.Type", Assert.Single(Calls(body)).Callee.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>foreach</c> over anything but <c>Cast&lt;E&gt;()</c> of the rewritten <c>E[]</c> enumerates as the control flow
    /// graph has it: through <c>GetEnumerator</c>, whatever the entry makes of a call inside the collection.
    /// </summary>
    [Theory]
    [InlineData("static int M(int k) { int n = 0; foreach (int d in Enumerable.Range(0, k)) { n++; } return n; }")]
    [InlineData("static int M(IEnumerable e) { int n = 0; foreach (DayOfWeek d in e.Cast<DayOfWeek>()) { n++; } return n; }")]
    [InlineData("static int M(Array e) { int n = 0; foreach (DayOfWeek d in e.Cast<DayOfWeek>()) { n++; } return n; }")]
    [InlineData("static int M(Type t) { int n = 0; foreach (DayOfWeek d in Enum.GetValues(t).Cast<DayOfWeek>()) { n++; } return n; }")]
    [InlineData("static int M(DayOfWeek d) { int n = 0; foreach (int x in Old.Wrap(Enum.IsDefined(typeof(DayOfWeek), d))) { n++; } return n; }", IsDefined)]
    [InlineData("static int M() { int n = 0; foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)).OfType<DayOfWeek>()) { n++; } return n; }", GetValues)]
    [InlineData("static int M() { int n = 0; foreach (ConsoleColor d in Enum.GetValues(typeof(DayOfWeek)).Cast<ConsoleColor>()) { n++; } return n; }", GetValues)]
    public void AnyOtherCollectionIsEnumerated(string method, params string[] expected)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten(method);

        Assert.Equal(expected, applied, StringComparer.Ordinal);
        Assert.Contains(Calls(body), static c => c.Callee.Value.Contains("::GetEnumerator()", StringComparison.Ordinal));
    }

    /// <summary>
    /// A <c>foreach</c> over a call that an entry rewrites and that is typed as an array is the index loop over the
    /// rewritten call, as it was before: the entry returns what the legacy member returns.
    /// </summary>
    [Fact]
    public void AForEachOverARewrittenCallTypedAsAnArrayIsItsIndexLoop()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Rewritten("static int M() { int n = 0; foreach (int x in Old.Make()) { n++; } return n; }");

        Assert.Equal(["test.made"], applied);
        Assert.Equal(IrText.Dump(Modern("static int M() { int n = 0; foreach (int x in Old.Made()) { n++; } return n; }")), IrText.Dump(body));
    }
}
