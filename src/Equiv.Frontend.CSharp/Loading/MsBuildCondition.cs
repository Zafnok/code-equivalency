using System.Globalization;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// MSBuild conditions over the grammar the bare loader supports (M3-029): <c>==</c>, <c>!=</c>, <c>&lt;</c>,
/// <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c> (numbers, or versions), <c>Exists</c>, <c>HasTrailingSlash</c>,
/// <c>and</c>, <c>or</c>, <c>!</c>, parentheses and boolean literals. <c>and</c> binds tighter than <c>or</c>, and both
/// short-circuit as in MSBuild, so an unsupported operand that is never reached costs nothing. Anything else throws
/// <see cref="UnsupportedConstructException"/>.
/// </summary>
internal sealed class MsBuildCondition
{
    private readonly string _text;
    private readonly List<string> _tokens;
    private int _position;

    private MsBuildCondition(string text)
    {
        _text = text;
        _tokens = Tokenize(text);
    }

    private abstract record Node;

    private sealed record Or(Node Left, Node Right) : Node;

    private sealed record And(Node Left, Node Right) : Node;

    private sealed record Not(Node Operand) : Node;

    private sealed record Function(string Name, string Argument) : Node;

    private sealed record Comparison(string Left, string Operator, string Right) : Node;

    private sealed record Operand(string Text) : Node;

    /// <summary>Whether <paramref name="condition"/> holds; an empty condition always does.</summary>
    /// <param name="directory">The directory <c>Exists</c> resolves a relative path against: that of the file holding the condition.</param>
    /// <exception cref="UnsupportedConstructException">The condition is outside the grammar, or depends on a value that is not exact.</exception>
    public static bool Evaluate(string? condition, MsBuildProperties properties, string directory)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        MsBuildCondition parser = new(condition);
        Node tree = parser.ParseOr();
        return parser._position == parser._tokens.Count ? Evaluate(tree, properties, directory) : throw parser.Unsupported();
    }

    private static bool Evaluate(Node node, MsBuildProperties properties, string directory) => node switch
    {
        Or or => Evaluate(or.Left, properties, directory) || Evaluate(or.Right, properties, directory),
        And and => Evaluate(and.Left, properties, directory) && Evaluate(and.Right, properties, directory),
        Not not => !Evaluate(not.Operand, properties, directory),
        Function { Name: "EXISTS" } exists => Value(exists.Argument, properties).Trim() is { Length: > 0 } path && ProjectPath.Resolve(directory, path) is not null,
        Function slash => Value(slash.Argument, properties) is [.., '/' or '\\'],
        Comparison comparison => Compare(Value(comparison.Left, properties), comparison.Operator, Value(comparison.Right, properties)),
        _ => Boolean(Value(((Operand)node).Text, properties)) ?? throw new UnsupportedConstructException($"a condition that is not a boolean: '{((Operand)node).Text}'"),
    };

    private static string Value(string operand, MsBuildProperties properties) => MsBuildProperties.Unescape(properties.Expand(operand).Exact);

    /// <summary>MSBuild's comparison: numbers compare as numbers, then booleans as booleans, then text ignoring case.</summary>
    private static bool Compare(string left, string op, string right)
    {
        if (op is "==" or "!=")
        {
            bool equal = Number(left) is { } l && Number(right) is { } r ? l == r
                : Boolean(left) is { } lb && Boolean(right) is { } rb ? lb == rb
                : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return equal == string.Equals(op, "==", StringComparison.Ordinal);
        }

        int order = Number(left) is { } ln && Number(right) is { } rn ? ln.CompareTo(rn)
            : Version.TryParse(left, out Version? lv) && Version.TryParse(right, out Version? rv) ? lv.CompareTo(rv)
            : throw new UnsupportedConstructException($"a comparison of '{left}' {op} '{right}', which are neither numbers nor versions");
        return op switch
        {
            "<" => order < 0,
            ">" => order > 0,
            "<=" => order <= 0,
            _ => order >= 0,
        };
    }

    private static double? Number(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long hex) ? hex
        : double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) ? number
        : null;

    private static bool? Boolean(string text) => text.ToUpperInvariant() switch
    {
        "TRUE" or "ON" or "YES" or "!FALSE" or "!OFF" or "!NO" => true,
        "FALSE" or "OFF" or "NO" or "!TRUE" or "!ON" or "!YES" => false,
        _ => null,
    };

    private Node ParseOr()
    {
        Node node = ParseAnd();
        while (Accept("or"))
        {
            node = new Or(node, ParseAnd());
        }

        return node;
    }

    private Node ParseAnd()
    {
        Node node = ParseUnary();
        while (Accept("and"))
        {
            node = new And(node, ParseUnary());
        }

        return node;
    }

    private Node ParseUnary()
    {
        if (Accept("!"))
        {
            return new Not(ParseUnary());
        }

        if (Accept("("))
        {
            Node inner = ParseOr();
            Expect(")");
            return inner;
        }

        string left = Next();
        if (Accept("("))
        {
            string name = left.ToUpperInvariant();
            string argument = Unquote(Next());
            Expect(")");
            return name is "EXISTS" or "HASTRAILINGSLASH" ? new Function(name, argument) : throw Unsupported();
        }

        return _position < _tokens.Count && _tokens[_position] is "==" or "!=" or "<" or ">" or "<=" or ">="
            ? new Comparison(Unquote(left), _tokens[_position++], Unquote(Next()))
            : new Operand(Unquote(left));
    }

    private bool Accept(string token)
    {
        if (_position < _tokens.Count && string.Equals(_tokens[_position], token, StringComparison.OrdinalIgnoreCase))
        {
            _position++;
            return true;
        }

        return false;
    }

    private void Expect(string token)
    {
        if (!Accept(token))
        {
            throw Unsupported();
        }
    }

    /// <summary>The next operand token; an operator or parenthesis there is a syntax error.</summary>
    private string Next() =>
        _position < _tokens.Count && _tokens[_position] is not ("(" or ")" or "!" or "==" or "!=" or "<" or ">" or "<=" or ">=" or ",")
            ? _tokens[_position++]
            : throw Unsupported();

    /// <summary>A quoted operand without its quotes; an unterminated quote is a syntax error.</summary>
    private string Unquote(string token) =>
        !token.StartsWith('\'') ? token
        : token.Length > 1 && token.EndsWith('\'') ? token[1..^1]
        : throw Unsupported();

    private UnsupportedConstructException Unsupported() => new($"a condition outside the supported grammar: \"{_text}\"");

    private static List<string> Tokenize(string text)
    {
        List<string> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int length = char.IsWhiteSpace(c) ? 1
                : c == '\'' ? QuotedLength(text, i)
                : c is '(' or ')' or ',' ? 1
                : c is '=' or '!' or '<' or '>' && i + 1 < text.Length && text[i + 1] == '=' ? 2
                : c is '!' or '<' or '>' or '=' ? 1
                : c == '$' && i + 1 < text.Length && text[i + 1] == '(' ? PropertyLength(text, i)
                : WordLength(text, i);
            if (!char.IsWhiteSpace(c))
            {
                tokens.Add(text.Substring(i, length));
            }

            i += length;
        }

        return tokens;
    }

    /// <summary>A quoted operand runs to the next quote; an unterminated one runs to the end, which the parser then rejects.</summary>
    private static int QuotedLength(string text, int start)
    {
        int end = text.IndexOf('\'', start + 1);
        return end < 0 ? text.Length - start : end - start + 1;
    }

    private static int PropertyLength(string text, int start)
    {
        int depth = 0;
        for (int i = start + 1; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i - start + 1;
            }
        }

        return text.Length - start;
    }

    private static int WordLength(string text, int start)
    {
        int i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '=' or '!' or '<' or '>' or '\'' or ','))
        {
            i++;
        }

        return i - start;
    }
}
