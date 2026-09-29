using Equiv.Corpus.Seeder;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit;

namespace Equiv.Corpus.Seeder.Tests;

/// <summary>
/// Ticket P2-036: <see cref="MutationOperator.RenameLocals"/> is in the preserving family, so it never renames a local
/// written inside an argument, where a <c>[CallerArgumentExpression]</c> parameter or <c>nameof</c> turns its name into a value.
/// Ticket P2-048: each cleanup operator applies at its site and refuses the sites its guard excludes.
/// </summary>
public sealed class SyntaxMutatorTests
{
    [Theory]
    [InlineData("int x = a; Check(x);")]
    [InlineData("int x = a; Check(x + 1);")]
    [InlineData("int x = a; string s = nameof(x); Check(s);")]
    [InlineData("int x = a; Check(Run(() => x));")]
    public void RenameLocals_SkipsALocalNamedInAnArgument(string body) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.RenameLocals, Method(body)));

    [Fact]
    public void RenameLocals_RenamesTheFirstLocalNotNamedInAnArgument()
    {
        MethodDeclarationSyntax renamed = SyntaxMutator.Apply(MutationOperator.RenameLocals, Method("int x = a; int y = x; Check(x);"), site: 0)!;

        Assert.Equal("{int x = a; int y0 = x; Check(x);}", renamed.Body!.ToString());
    }

    [Fact]
    public void RenameLocals_StillRenamesALocalNeverInAnArgument() =>
        Assert.Equal(1, SyntaxMutator.Sites(MutationOperator.RenameLocals, Method("int x = a; int y = x + 1;")));

    [Fact]
    public void ThePreservingFamilyIsTheSixM0012OperatorsAndTheFiveCleanupOnes() =>
        Assert.Equal(
            [
                MutationOperator.RenameLocals, MutationOperator.ReorderIndependentStatements, MutationOperator.InvertIf, MutationOperator.Commute,
                MutationOperator.IntroduceTemporary, MutationOperator.InlineTemporary, MutationOperator.IfToConditional, MutationOperator.CoalesceNullCheck,
                MutationOperator.ConcatToInterpolation, MutationOperator.GuardClause, MutationOperator.ForToForeach,
            ],
            Enum.GetValues<MutationOperator>().Where(SyntaxMutator.IsPreserving));

    [Theory]
    [InlineData("int M(bool c, int a, int b) { int x; if (c) x = a; else { x = b; } return x; }", "x = c ? a : b;")]
    [InlineData("int M(bool c, int a, int b) { if (c) { return a; } else { return b; } }", "return c ? a : b;")]
    [InlineData("int M(int a, int b) { if (a < b) return a + 1; else return b; }", "return (a < b) ? (a + 1) : b;")]
    public void IfToConditional_JoinsTwoBranchesOfOneType(string method, string expected) =>
        Assert.Contains(expected, Applied(MutationOperator.IfToConditional, method), StringComparison.Ordinal);

    [Theory]
    [InlineData("long M(bool c, int a, long b) { long x; if (c) x = a; else x = b; return x; }")]
    [InlineData("int? M(bool c, int a) { if (c) return a; else return null; }")]
    [InlineData("static int F; void M(bool c) { if (c) F = 1; else F = 2; }")]
    [InlineData("int M(bool c, int a) { int x; if (c) { x = a; x = a; } else x = a; return x; }")]
    [InlineData("async System.Threading.Tasks.Task<int> M(bool c, int a) { await System.Threading.Tasks.Task.Yield(); if (c) return a; else return a; }")]
    public void IfToConditional_RefusesAConversionAFieldOrAnotherShape(string method) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.IfToConditional, Declared(method)));

    [Theory]
    [InlineData("string M(string s, string t) { return s != null ? s : t; }", "return s ?? t;")]
    [InlineData("string M(string s, string t) { return (s == null) ? t + t : s; }", "return s ?? (t + t);")]
    [InlineData("object M(object s, object t) { return null != s ? s : t; }", "return s ?? t;")]
    public void CoalesceNullCheck_BecomesACoalesce(string method, string expected) =>
        Assert.Contains(expected, Applied(MutationOperator.CoalesceNullCheck, method), StringComparison.Ordinal);

    [Theory]
    [InlineData("object M(string s, object t) { return s != null ? s : t; }")]
    [InlineData("int? M(int? s, int? t) { return s != null ? s : t; }")]
    [InlineData("static string F; string M(string t) { return F != null ? F : t; }")]
    [InlineData("sealed class D { public static bool operator ==(D a, D b) => true; public static bool operator !=(D a, D b) => false; public override bool Equals(object o) => true; public override int GetHashCode() => 0; } D M(D s, D t) { return s != null ? s : t; }")]
    public void CoalesceNullCheck_RefusesAnotherTypeAFieldOrAUserDefinedEquality(string method) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.CoalesceNullCheck, Declared(method)));

    [Theory]
    [InlineData("string M(string s, string t) { return \"a{\" + s + t; }", "return $\"a{{{s}{t}\";")]
    [InlineData("string M(string s, string t) { return s + (s != null ? t : s); }", "return $\"{s}{(s != null ? t : s)}\";")]
    [InlineData("string M(string s, string[] t) { return s + t[0] + @\"x\"; }", "return $\"{s}{t[0]}{@\"x\"}\";")]
    public void ConcatToInterpolation_JoinsStringOperands(string method, string expected) =>
        Assert.Contains(expected, Applied(MutationOperator.ConcatToInterpolation, method), StringComparison.Ordinal);

    [Theory]
    [InlineData("string M(string s, int n) { return s + n; }")]
    [InlineData("string M() { return \"a\" + \"b\"; }")]
    [InlineData("string M(string s, object o) { return s + o; }")]
    public void ConcatToInterpolation_RefusesANonStringOperandOrAConstant(string method) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.ConcatToInterpolation, Declared(method)));

    [Fact]
    public void GuardClause_ReturnsEarlyAndHoistsTheBody()
    {
        MethodDeclarationSyntax guarded = SyntaxMutator.Apply(MutationOperator.GuardClause, Declared("void M(bool c, int[] u) { u[0] = 1; if (c && u.Length > 1) { int t = 2; u[1] = t; } }"), site: 0)!;

        Assert.Equal(
            ["u[0] = 1;", "if (!(c && u.Length > 1))\n    return;", "int t = 2;", "u[1] = t;"],
            guarded.Body!.Statements.Select(static s => s.ToString().ReplaceLineEndings("\n")),
            StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("int M(bool c) { if (c) { return 1; } return 0; }")]
    [InlineData("void M(bool c, int[] u) { if (c) { u[0] = 1; } else { u[0] = 2; } }")]
    [InlineData("void M(bool c, int[] u) { if (c) { u[0] = 1; } u[1] = 2; }")]
    [InlineData("void M(bool c, int[] u) { { int t = 1; u[0] = t; } if (c) { int t = 2; u[1] = t; } }")]
    [InlineData("void M(dynamic c, int[] u) { if (c) { u[0] = 1; } }")]
    public void GuardClause_RefusesANonVoidMethodAnElseAnotherLastStatementAClashOrANonBoolCondition(string method) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.GuardClause, Declared(method)));

    [Theory]
    [InlineData("int M(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i] * a[i]; } return s; }", "foreach (var item in a)", "s += item * item;")]
    [InlineData("int M(string[] a, int item) { int s = 0; for (int i = 0; i < a.Length; ++i) s += a[i].Length; return s + item; }", "foreach (var item0 in a)", "s += item0.Length;")]
    public void ForToForeach_IteratesTheElements(string method, string header, string body)
    {
        string applied = Applied(MutationOperator.ForToForeach, method);

        Assert.Contains(header, applied, StringComparison.Ordinal);
        Assert.Contains(body, applied, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int M(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i] + i; } return s; }")]
    [InlineData("int M(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i]; a = new int[0]; } return s; }")]
    [InlineData("void M(int[] a) { for (int i = 0; i < a.Length; i++) { a[i] = 0; } }")]
    [InlineData("void M(int[] a) { for (int i = 0; i < a.Length; i++) { a[i]++; } }")]
    [InlineData("int M(string a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i]; } return s; }")]
    [InlineData("int M(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { System.Func<int> f = () => a[i]; s += f(); } return s; }")]
    [InlineData("int M(int[] a) { System.Action f = () => a = null; int s = 0; for (int i = 0; i < a.Length; i++) { f(); s += a[i]; } return s; }")]
    [InlineData("int M(ref int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i]; } return s; }")]
    [InlineData("int M(int[] a) { int s = 0; for (int i = 1; i < a.Length; i++) { s += a[i]; } return s; }")]
    [InlineData("int M(System.DateTime[] a) { int s = 0; for (int i = 0; i < a.Length; i++) { s += a[i].Day; } return s; }")]
    public void ForToForeach_RefusesAnIndexUseAWriteANonArrayACaptureOrAnotherStart(string method) =>
        Assert.Equal(0, SyntaxMutator.Sites(MutationOperator.ForToForeach, Declared(method)));

    [Theory]
    [InlineData(MutationOperator.IfToConditional, "int M(bool c, int a, int b) { int x; if (c) x = a; else x = b; return x; }")]
    [InlineData(MutationOperator.CoalesceNullCheck, "string M(string s, string t) { return s == null ? t : s; }")]
    [InlineData(MutationOperator.ConcatToInterpolation, "string M(string s, string t) { return \"}\" + s + t; }")]
    [InlineData(MutationOperator.GuardClause, "void M(bool c, int[] u) { if (c) u[0] = 1; }")]
    [InlineData(MutationOperator.ForToForeach, "int M(int[] a) { int s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }")]
    public void CleanupOperatorsCompile(MutationOperator op, string method)
    {
        MethodDeclarationSyntax declared = Declared(method);
        SyntaxNode root = declared.SyntaxTree.GetRoot(TestContext.Current.CancellationToken);
        string mutated = root.ReplaceNode(declared, SyntaxMutator.Apply(op, declared, site: 0)!).ToFullString();

        Assert.False(string.Equals(root.ToFullString(), mutated, StringComparison.Ordinal));
        Assert.True(CompileCheck.StillCompiles(root.ToFullString(), mutated), mutated);
    }

    private static string Applied(MutationOperator op, string method) => SyntaxMutator.Apply(op, Declared(method), site: 0)!.ToFullString();

    /// <summary>The method <c>M</c> among <paramref name="members"/>, declared in a class of its own.</summary>
    private static MethodDeclarationSyntax Declared(string members) =>
        CSharpSyntaxTree.ParseText($"class C {{ {members} }}", cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(static m => string.Equals(m.Identifier.Text, "M", StringComparison.Ordinal));

    private static MethodDeclarationSyntax Method(string body) =>
        CSharpSyntaxTree.ParseText($$"""class C { void M(int a) {{{body}}} }""", cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
}
