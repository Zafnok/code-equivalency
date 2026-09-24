using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

/// <summary>Ticket M3-030 acceptance criterion 1: the census's token proxy for congruence (ADR 0034).</summary>
public sealed class SyntaxTokensTests
{
    private const string Legacy = """
        namespace N
        {
            public class C
            {
                public int Same(int a) { return a + 1; }
                public int Changed(int a) { return a + 1; }
            }
        }
        """;

    // Same differs only in trivia: layout, a comment, and a directive.
    private const string Modern = """
        namespace N
        {
            public class C
            {
                public int Same(int a)
                {
                    // Reformatted.
        #region R
                    return a+1;
        #endregion
                }

                public int Changed(int a) { return 1 + a; }
            }
        }
        """;

    [Theory]
    [InlineData("Same", true)]
    [InlineData("Changed", false)]
    [InlineData(".ctor", true)]
    public void TokensAreComparedIgnoringTrivia(string name, bool expected) =>
        Assert.Equal(expected, SyntaxTokens.Equal(Method(Legacy, name), Method(Modern, name)));

    private static IMethodSymbol Method(string source, string name) =>
        RoslynTestCompilations.Compile(source).GetTypeByMetadataName("N.C")!.GetMembers(name).OfType<IMethodSymbol>().Single();
}
