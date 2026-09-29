using Equiv.Corpus.Seeder;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit;

namespace Equiv.Corpus.Seeder.Tests;

/// <summary>
/// Ticket P2-036: <see cref="MutationOperator.RenameLocals"/> is in the preserving family, so it never renames a local
/// written inside an argument, where a <c>[CallerArgumentExpression]</c> parameter or <c>nameof</c> turns its name into a value.
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

    private static MethodDeclarationSyntax Method(string body) =>
        CSharpSyntaxTree.ParseText($$"""class C { void M(int a) {{{body}}} }""", cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
}
