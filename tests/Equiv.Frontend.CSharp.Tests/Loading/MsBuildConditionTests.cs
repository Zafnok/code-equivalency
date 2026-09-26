using CsCheck;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class MsBuildConditionTests : IDisposable
{
    private static readonly Dictionary<string, string> Globals = new(StringComparer.OrdinalIgnoreCase) { ["P"] = "a", ["Q"] = "B" };

    private static readonly Gen<string> Operands = Gen.OneOfConst("a", "A", "b", "B", "c d", string.Empty, "$(P)", "$(Q)", "$(Unset)");

    private static readonly Gen<string> Keyword = Gen.OneOfConst("and", "AND", "And", "or", "OR", "Or");

    private static readonly Gen<Node> Trees = Tree(4);

    private readonly BareFixture _fixture = new();

    private abstract record Node;

    private sealed record Leaf(string Left, bool Equal, string Right) : Node;

    private sealed record Not(Node Operand) : Node;

    private sealed record Binary(Node Left, bool And, Node Right, bool ExtraParentheses) : Node;

    [Fact]
    public void ConditionGrammar() =>
        Gen.Select(Trees, Keyword, Keyword).Sample(
            static t =>
            {
                (Node tree, string and, string or) = t;
                Assert.Equal(Reference(tree), MsBuildCondition.Evaluate(Render(tree, and, or, parent: null), Properties(), "."));
            },
            iter: 1000,
            print: static t => Render(t.Item1, t.Item2, t.Item3, parent: null));

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("true", true)]
    [InlineData("'On'", true)]
    [InlineData("'no'", false)]
    [InlineData("'!false'", true)]
    [InlineData("'!yes'", false)]
    [InlineData("'1.0' == '1'", true)]
    [InlineData("'0x10' == '16'", true)]
    [InlineData("'true' == 'yes'", true)]
    [InlineData("'4.8' > '4.7.2'", true)]
    [InlineData("'4.7.2' >= '4.8'", false)]
    [InlineData("'4.5.1' < '4.6'", true)]
    [InlineData("'3' <= '3'", true)]
    [InlineData("'10' > '9'", true)]
    [InlineData("HasTrailingSlash('dir\\')", true)]
    [InlineData("hastrailingslash('dir/')", true)]
    [InlineData("HasTrailingSlash('dir')", false)]
    [InlineData("'%3B' == ';'", true)]
    [InlineData("'$(P)|$(Q)' == 'A|b'", true)]
    [InlineData("$(P) == a", true)]
    [InlineData("!('a' == 'b') and ('a' != 'b' or false)", true)]
    public void Evaluates(string condition, bool expected) =>
        Assert.Equal(expected, MsBuildCondition.Evaluate(condition, Properties(), "."));

    [Fact]
    public void ExistsResolvesAgainstTheGivenDirectoryIgnoringCaseAndSeparators()
    {
        _fixture.Write(Path.Combine("dir", "Sub", "File.targets"), "<Project />");
        string directory = Path.Combine(_fixture.Root, "dir");

        Assert.True(MsBuildCondition.Evaluate(@"Exists('sub\file.TARGETS')", Properties(), directory));
        Assert.True(MsBuildCondition.Evaluate("!Exists('missing.targets')", Properties(), directory));
        Assert.False(MsBuildCondition.Evaluate("Exists('  ')", Properties(), directory));
        Assert.True(MsBuildCondition.Evaluate("Exists($(MSBuildThisFileDirectory))", Properties(("MSBuildThisFileDirectory", directory)), _fixture.Root));
    }

    [Theory]
    [InlineData("'a' =~ 'b'")]
    [InlineData("'a' == ")]
    [InlineData("'a")]
    [InlineData("('a' == 'a'")]
    [InlineData("'a' == 'a')")]
    [InlineData("Exists2('x')")]
    [InlineData("Exists('a', 'b')")]
    [InlineData("Exists()")]
    [InlineData("'a' and")]
    [InlineData("== 'a'")]
    [InlineData("!")]
    public void AConditionOutsideTheGrammarIsUnsupported(string condition)
    {
        UnsupportedConstructException exception = Assert.Throws<UnsupportedConstructException>(() => MsBuildCondition.Evaluate(condition, Properties(), "."));

        Assert.Equal($"a condition outside the supported grammar: \"{condition}\"", exception.Construct);
    }

    [Theory]
    [InlineData("'a'", "a condition that is not a boolean: 'a'")]
    [InlineData("$(Unclosed == 'a'", "a condition that is not a boolean: '$(Unclosed == 'a''")]
    [InlineData("'v4.5' > 'v4.0'", "a comparison of 'v4.5' > 'v4.0', which are neither numbers nor versions")]
    [InlineData("'$([System.IO.Path]::GetFullPath(x))' == ''", "the property function $([System.IO.Path]::GetFullPath(x))")]
    public void AnOperandThatCannotBeEvaluatedIsUnsupported(string condition, string construct) =>
        Assert.Equal(construct, Assert.Throws<UnsupportedConstructException>(() => MsBuildCondition.Evaluate(condition, Properties(), ".")).Construct);

    [Theory]
    [InlineData("'a' == 'b' and $([MSBuild]::Unknown())", false)]
    [InlineData("'a' == 'a' or $([MSBuild]::Unknown())", true)]
    public void AndAndOrShortCircuitPastAnUnsupportedOperand(string condition, bool expected) =>
        Assert.Equal(expected, MsBuildCondition.Evaluate(condition, Properties(), "."));

    public void Dispose() => _fixture.Dispose();

    private static MsBuildProperties Properties(params (string Name, string Value)[] extra)
    {
        Dictionary<string, string> global = new(Globals, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in extra)
        {
            global[name] = value;
        }

        return new MsBuildProperties(global, static _ => null);
    }

    private static Gen<Node> Tree(int depth)
    {
        Gen<Node> leaf = Gen.Select(Operands, Gen.Bool, Operands, static (l, e, r) => (Node)new Leaf(l, e, r));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<Node> smaller = Tree(depth - 1);
        return Gen.Frequency(
            (3, leaf),
            (1, smaller.Select(static n => (Node)new Not(n))),
            (2, Gen.Select(smaller, Gen.Bool, smaller, Gen.Bool, static (l, a, r, p) => (Node)new Binary(l, a, r, p))));
    }

    private static bool Reference(Node node) => node switch
    {
        Leaf leaf => string.Equals(Value(leaf.Left), Value(leaf.Right), StringComparison.OrdinalIgnoreCase) == leaf.Equal,
        Not not => !Reference(not.Operand),
        Binary { And: true } binary => Reference(binary.Left) && Reference(binary.Right),
        _ => Reference(((Binary)node).Left) || Reference(((Binary)node).Right),
    };

    private static string Value(string operand) => operand switch
    {
        "$(P)" => "a",
        "$(Q)" => "B",
        "$(Unset)" => string.Empty,
        _ => operand,
    };

    /// <summary>
    /// MSBuild text for <paramref name="node"/>, with parentheses only where precedence needs them (an <c>or</c> under
    /// an <c>and</c>, and every <c>!</c> operand) plus the redundant ones the generator asks for.
    /// </summary>
    private static string Render(Node node, string and, string or, Binary? parent) => node switch
    {
        Leaf leaf => $"'{leaf.Left}' {(leaf.Equal ? "==" : "!=")} '{leaf.Right}'",
        Not not => $"!({Render(not.Operand, and, or, parent: null)})",
        Binary binary => Parenthesise(
            $"{Render(binary.Left, and, or, binary)} {(binary.And ? Operator(and, "and") : Operator(or, "or"))} {Render(binary.Right, and, or, binary)}",
            binary.ExtraParentheses || (parent is { And: true } && !binary.And)),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>The generated keyword when it is the right one (in whatever case), else the plain one.</summary>
    private static string Operator(string generated, string keyword) =>
        string.Equals(generated, keyword, StringComparison.OrdinalIgnoreCase) ? generated : keyword;

    private static string Parenthesise(string text, bool parenthesise) => parenthesise ? $"({text})" : text;
}
