using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Equiv.Core.Ir;

/// <summary>
/// Recursive-descent parser for <see cref="IrText"/>. A first pass collects every definition
/// (a variable followed by an optional source name and <c>:</c>) so that uses, which may
/// appear before their definition in text order (phis on back edges), resolve to it.
/// </summary>
internal sealed class IrTextParser
{
    private static readonly FrozenDictionary<string, IrType> SimpleTypes = new Dictionary<string, IrType>(StringComparer.Ordinal)
    {
        ["bool"] = new IrBool(),
        ["bv8"] = new IrBitVec(8),
        ["bv16"] = new IrBitVec(16),
        ["bv32"] = new IrBitVec(32),
        ["bv64"] = new IrBitVec(64),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<char, char> Escapes =
        new Dictionary<char, char> { ['\\'] = '\\', ['"'] = '"', ['n'] = '\n' }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, IrBinaryOp> BinaryOps =
        IrText.BinaryNames.ToFrozenDictionary(static n => n.Name, static n => n.Op, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, IrOverflowOp> OverflowOps =
        IrText.OverflowNames.ToFrozenDictionary(static n => n.Name, static n => n.Op, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, IrUnaryOp> UnaryOps =
        IrText.UnaryNames.ToFrozenDictionary(static n => n.Name, static n => n.Op, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Func<IrTextParser, IrVar, IrInstruction>> Assignments =
        new Dictionary<string, Func<IrTextParser, IrVar, IrInstruction>>(StringComparer.Ordinal)
        {
            ["const"] = static (p, target) => new IrConst(target, p.ParseLiteral()),
            ["overflows"] = static (p, target) => new IrOverflows(target, p.ParseOverflowOp(), p.ParseUse(), p.ParseNextUse()),
            ["phi"] = static (p, target) => new IrPhi(target, p.ParseList("[", "]", p.ParseIncoming)),
            ["call"] = static (p, target) => p.ParseCall(target),
            ["mapread"] = static (p, target) => new IrMapRead(target, p.ParseUse(), p.ParseNextUse()),
            ["mapwrite"] = static (p, target) => new IrMapWrite(target, p.ParseUse(), p.ParseNextUse(), p.ParseNextUse()),
            ["opaque"] = static (p, target) => p.ParseOpaque(target),
            ["pure"] = static (p, target) => p.ParsePure(target),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Func<IrTextParser, IrInstruction>> Statements =
        new Dictionary<string, Func<IrTextParser, IrInstruction>>(StringComparer.Ordinal)
        {
            ["call"] = static p => p.ParseCall(target: null),
            ["opaque"] = static p => p.ParseOpaque(target: null),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, Func<IrTextParser, IrTerminator>> Terminators =
        new Dictionary<string, Func<IrTextParser, IrTerminator>>(StringComparer.Ordinal)
        {
            ["goto"] = static p => new IrGoto(p.ParseBlockId()),
            ["br"] = static p => new IrBranch(p.ParseUse(), p.ParseNextBlockId(), p.ParseNextBlockId()),
            ["switch"] = static p => p.ParseSwitch(),
            ["ret"] = static p => new IrReturn(p.Peek.Kind == IrTokenKind.Var ? p.ParseUse() : null, p.ParseOuts()),
            ["throw"] = static p => new IrThrow(p.ExpectString(), p.ParseOuts()),
            ["unreachable"] = static _ => new IrUnreachable(),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly List<IrToken> tokens;
    private readonly Dictionary<string, IrVar> definitions;
    private int position;

    private IrTextParser(List<IrToken> tokens, Dictionary<string, IrVar> definitions, int position)
    {
        this.tokens = tokens;
        this.definitions = definitions;
        this.position = position;
    }

    private enum IrTokenKind
    {
        Word,
        Var,
        String,
        Number,
        Symbol,
        End,
    }

    private IrToken Peek => tokens[position];

    public static IrProcedure Parse(string text)
    {
        List<IrToken> all = Tokenize(text);
        Dictionary<string, IrVar> defined = new(StringComparer.Ordinal);
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Kind == IrTokenKind.Var && IsDefinitionSuffix(all, i + 1))
            {
                IrVar var = new IrTextParser(all, defined, i).ParseDefinition();
                defined.TryAdd(var.Name, var);
            }
        }

        return new IrTextParser(all, defined, 0).ParseProcedure();
    }

    private static bool IsDefinitionSuffix(List<IrToken> all, int index) =>
        IsSymbol(all[index], ":") || (all[index].Kind == IrTokenKind.String && IsSymbol(all[index + 1], ":"));

    private static bool IsSymbol(IrToken token, string text) => token.Kind == IrTokenKind.Symbol && string.Equals(token.Text, text, StringComparison.Ordinal);

    private static bool IsWord(IrToken token, string text) => token.Kind == IrTokenKind.Word && string.Equals(token.Text, text, StringComparison.Ordinal);

    private static IrParseException Fail(IrToken token, string message) => new(message, token.Line, token.Column);

    private static List<IrToken> Tokenize(string text)
    {
        List<IrToken> result = [];
        int i = 0;
        int line = 1;
        int column = 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                i++;
                line++;
                column = 1;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                column++;
                continue;
            }

            int start = i;
            (IrTokenKind kind, string value) = ReadToken(text, ref i, line, column);
            result.Add(new IrToken(kind, value, line, column));
            column += i - start;
        }

        result.Add(new IrToken(IrTokenKind.End, string.Empty, line, column));
        return result;
    }

    /// <summary>Reads one token starting at <paramref name="i"/> and leaves <paramref name="i"/> after it.</summary>
    private static (IrTokenKind Kind, string Value) ReadToken(string text, ref int i, int line, int column)
    {
        char c = text[i];
        if (c == '%')
        {
            return ReadVariable(text, ref i, line, column);
        }

        if (c == '"')
        {
            return (IrTokenKind.String, ReadString(text, ref i, line, column));
        }

        if (char.IsAsciiDigit(c) || char.IsAsciiLetter(c) || c == '_')
        {
            return ReadWordOrNumber(text, ref i);
        }

        if (text.AsSpan(i).StartsWith("->", StringComparison.Ordinal))
        {
            i += 2;
            return (IrTokenKind.Symbol, "->");
        }

        if (!"()[],:=<>-!".Contains(c, StringComparison.Ordinal))
        {
            throw new IrParseException($"unexpected character '{c}'", line, column);
        }

        i++;
        return (IrTokenKind.Symbol, c.ToString());
    }

    /// <summary>Reads a <c>%name</c> variable token starting at <paramref name="i"/>, which points at the <c>%</c>.</summary>
    private static (IrTokenKind Kind, string Value) ReadVariable(string text, ref int i, int line, int column)
    {
        int start = i;
        i++;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '.' or '$'))
        {
            i++;
        }

        return i == start + 1
            ? throw new IrParseException("expected a variable name after '%'", line, column)
            : (IrTokenKind.Var, text[(start + 1)..i]);
    }

    /// <summary>Reads a word or number token starting at <paramref name="i"/>, which points at its first character.</summary>
    private static (IrTokenKind Kind, string Value) ReadWordOrNumber(string text, ref int i)
    {
        int start = i;
        IrTokenKind kind = char.IsAsciiDigit(text[i]) ? IrTokenKind.Number : IrTokenKind.Word;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        return (kind, text[start..i]);
    }

    private static string ReadString(string text, ref int i, int line, int column)
    {
        StringBuilder value = new();
        i++;
        while (true)
        {
            if (i >= text.Length || text[i] == '\n')
            {
                throw new IrParseException("unterminated string", line, column);
            }

            char c = text[i++];
            if (c == '"')
            {
                return value.ToString();
            }

            if (c == '\\')
            {
                if (i >= text.Length || !Escapes.TryGetValue(text[i], out char escaped))
                {
                    throw new IrParseException("unknown escape in string", line, column);
                }

                i++;
                c = escaped;
            }

            value.Append(c);
        }
    }

    private IrProcedure ParseProcedure()
    {
        ExpectWord("proc");
        string identity = ExpectString();
        ImmutableArray<IrParameter> parameters = ParseList("(", ")", ParseParameter);
        IrType? returnType = AcceptSymbol("->") ? ParseType() : null;
        ExpectWord("entry");
        IrBlockId entry = ParseBlockId();
        List<IrBlock> blocks = [];
        while (Peek.Kind != IrTokenKind.End)
        {
            blocks.Add(ParseBlock());
        }

        return new IrProcedure(new ProcedureIdentity(identity), parameters, returnType, [.. blocks], entry);
    }

    private IrParameter ParseParameter()
    {
        bool isRef = AcceptWord("ref");
        bool isOut = !isRef && AcceptWord("out");
        IrParameterKind kind = (isRef, isOut) switch
        {
            (true, _) => IrParameterKind.Ref,
            (_, true) => IrParameterKind.Out,
            _ => IrParameterKind.In,
        };
        return new IrParameter(ParseDefinition(), kind);
    }

    private IrVar ParseDefinition()
    {
        string name = Expect(IrTokenKind.Var, "a variable").Text;
        string? source = Peek.Kind == IrTokenKind.String ? ExpectString() : null;
        ExpectSymbol(":");
        return new IrVar(name, ParseType(), source);
    }

    private IrType ParseType()
    {
        IrToken token = Expect(IrTokenKind.Word, "a type");
        if (SimpleTypes.TryGetValue(token.Text, out IrType? simple))
        {
            return simple;
        }

        if (string.Equals(token.Text, "sort", StringComparison.Ordinal))
        {
            return new IrSort(ExpectString());
        }

        if (!string.Equals(token.Text, "map", StringComparison.Ordinal))
        {
            throw Fail(token, $"unknown type '{token.Text}'");
        }

        ExpectSymbol("<");
        IrType key = ParseType();
        ExpectSymbol(",");
        IrType value = ParseType();
        ExpectSymbol(">");
        return new IrMap(key, value);
    }

    private IrValue ParseLiteral()
    {
        IrType type = ParseType();
        return type switch
        {
            IrBool => new IrBoolValue(ParseBool()),
            IrBitVec bitVec => ParseBits(bitVec.Width),
            IrSort sort => new IrSortValue(sort.Name, ParseInt()),
            _ => ParseMap((IrMap)type),
        };
    }

    private bool ParseBool()
    {
        IrToken token = Expect(IrTokenKind.Word, "true or false");
        return token.Text switch
        {
            "true" => true,
            "false" => false,
            _ => throw Fail(token, $"expected true or false but found '{token.Text}'"),
        };
    }

    private IrBitVecValue ParseBits(int width)
    {
        bool negative = AcceptSymbol("-");
        IrToken token = Expect(IrTokenKind.Number, "a number");
        ulong limit = negative ? 1UL << (width - 1) : IrBits.Mask(width);
        bool fits = ulong.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong magnitude) && magnitude <= limit;
        ulong signed = negative ? 0 - magnitude : magnitude;
        return fits
            ? new IrBitVecValue(width, signed & IrBits.Mask(width))
            : throw Fail(token, $"literal does not fit in {width.ToString(CultureInfo.InvariantCulture)} bits");
    }

    private IrMapValue ParseMap(IrMap type)
    {
        ImmutableArray<KeyValuePair<IrValue, IrValue>> entries = ParseList("[", "]", ParseMapEntry);
        ExpectWord("default");
        IrValue defaultValue = ParseLiteral();
        return new IrMapValue(type, defaultValue, ImmutableDictionary<IrValue, IrValue>.Empty.SetItems(entries));
    }

    private KeyValuePair<IrValue, IrValue> ParseMapEntry()
    {
        IrValue key = ParseLiteral();
        ExpectSymbol("->");
        return new KeyValuePair<IrValue, IrValue>(key, ParseLiteral());
    }

    private int ParseInt()
    {
        IrToken token = Expect(IrTokenKind.Number, "a number");
        return int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw Fail(token, "number out of range");
    }

    private IrBlockId ParseBlockId()
    {
        IrToken token = Expect(IrTokenKind.Word, "a block id");
        return token.Text.StartsWith('B') && int.TryParse(token.Text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? new IrBlockId(value)
            : throw Fail(token, $"expected a block id like B0 but found '{token.Text}'");
    }

    private IrBlockId ParseNextBlockId()
    {
        ExpectSymbol(",");
        return ParseBlockId();
    }

    private IrBlock ParseBlock()
    {
        IrBlockId id = ParseBlockId();
        ExpectSymbol(":");
        List<IrInstruction> instructions = [];
        while (true)
        {
            IrToken token = Peek;
            if (token.Kind == IrTokenKind.Var)
            {
                instructions.Add(ParseAssignment());
            }
            else if (token.Kind == IrTokenKind.Word && Statements.TryGetValue(token.Text, out Func<IrTextParser, IrInstruction>? statement))
            {
                position++;
                instructions.Add(statement(this));
            }
            else
            {
                return new IrBlock(id, [.. instructions], ParseTerminator());
            }
        }
    }

    private IrInstruction ParseAssignment()
    {
        IrVar target = ParseDefinition();
        ExpectSymbol("=");
        IrToken op = Expect(IrTokenKind.Word, "an instruction");
        bool isAssignment = Assignments.TryGetValue(op.Text, out Func<IrTextParser, IrVar, IrInstruction>? parse);
        bool isBinary = BinaryOps.TryGetValue(op.Text, out IrBinaryOp binary);
        bool isUnary = UnaryOps.TryGetValue(op.Text, out IrUnaryOp unary);
        return (isAssignment, isBinary, isUnary) switch
        {
            (true, _, _) => parse!(this, target),
            (_, true, _) => new IrBinary(target, binary, ParseUse(), ParseNextUse()),
            (_, _, true) => new IrUnary(target, unary, ParseUse()),
            _ => throw Fail(op, $"unknown instruction '{op.Text}'"),
        };
    }

    private IrOverflowOp ParseOverflowOp()
    {
        IrToken token = Expect(IrTokenKind.Word, "an overflow operation");
        return OverflowOps.TryGetValue(token.Text, out IrOverflowOp op) ? op : throw Fail(token, $"unknown overflow operation '{token.Text}'");
    }

    private (IrBlockId From, IrVar Value) ParseIncoming()
    {
        IrBlockId from = ParseBlockId();
        ExpectSymbol(":");
        return (from, ParseUse());
    }

    private IrCall ParseCall(IrVar? target)
    {
        string calleeValue = ExpectString();
        CallIdentity callee = new(calleeValue, AcceptSymbol("!"));
        ImmutableArray<IrVar> args = ParseList("(", ")", ParseUse);
        IrVar? threw = AcceptWord("threw") ? ParseDefinition() : null;
        return new IrCall(target, threw, callee, args) { Heap = AcceptWord("heap") ? ParseList("(", ")", ParseHeapPair) : [] };
    }

    private IrHeapPair ParseHeapPair()
    {
        string map = ExpectString();
        IrVar before = ParseUse();
        ExpectSymbol("->");
        return new IrHeapPair(map, before, ParseDefinition());
    }

    private IrPure ParsePure(IrVar target)
    {
        string function = ExpectString();
        bool runtimeSensitive = AcceptSymbol("!");
        ImmutableArray<IrVar> args = ParseList("(", ")", ParseUse);
        ImmutableArray<IrPureThrow> throws = AcceptWord("throws") ? ParseList("(", ")", ParsePureThrow) : [];
        return new IrPure(target, throws, function, args) { RuntimeSensitive = runtimeSensitive };
    }

    private IrPureThrow ParsePureThrow() => new(ParseDefinition(), ExpectString());

    private IrOpaque ParseOpaque(IrVar? target)
    {
        bool wholeBody = AcceptWord("body");
        string reason = ExpectString();
        ExpectWord("at");
        string path = ExpectString();
        int startLine = ParseInt();
        ExpectSymbol(":");
        int startColumn = ParseInt();
        ExpectSymbol("-");
        int endLine = ParseInt();
        ExpectSymbol(":");
        return new IrOpaque(target, reason, new SourceSpan(path, startLine, startColumn, endLine, ParseInt())) { WholeBody = wholeBody };
    }

    private IrTerminator ParseTerminator()
    {
        IrToken token = Expect(IrTokenKind.Word, "an instruction or terminator");
        return Terminators.TryGetValue(token.Text, out Func<IrTextParser, IrTerminator>? parse)
            ? parse(this)
            : throw Fail(token, $"expected an instruction or terminator but found '{token.Text}'");
    }

    private IrSwitch ParseSwitch()
    {
        IrVar scrutinee = ParseUse();
        ImmutableArray<(IrValue Value, IrBlockId Target)> cases = ParseList("[", "]", ParseCase);
        ExpectWord("default");
        return new IrSwitch(scrutinee, cases, ParseBlockId());
    }

    private (IrValue Value, IrBlockId Target) ParseCase()
    {
        IrValue value = ParseLiteral();
        ExpectSymbol("->");
        return (value, ParseBlockId());
    }

    private ImmutableArray<IrOut> ParseOuts() => AcceptWord("outs") ? ParseList("(", ")", ParseOut) : [];

    private IrOut ParseOut()
    {
        IrVar param = ParseUse();
        ExpectSymbol("=");
        return new IrOut(param, ParseUse());
    }

    private IrVar ParseUse()
    {
        IrToken token = Expect(IrTokenKind.Var, "a variable");
        return definitions.TryGetValue(token.Text, out IrVar? var)
            ? var
            : throw Fail(token, $"undefined variable %{token.Text}");
    }

    private IrVar ParseNextUse()
    {
        ExpectSymbol(",");
        return ParseUse();
    }

    private ImmutableArray<T> ParseList<T>(string open, string close, Func<T> item)
    {
        ExpectSymbol(open);
        if (AcceptSymbol(close))
        {
            return [];
        }

        ImmutableArray<T>.Builder items = ImmutableArray.CreateBuilder<T>();
        do
        {
            items.Add(item());
        }
        while (AcceptSymbol(","));

        ExpectSymbol(close);
        return items.ToImmutable();
    }

    private IrToken Expect(IrTokenKind kind, string what)
    {
        IrToken token = Peek;
        if (token.Kind != kind)
        {
            throw Fail(token, $"expected {what} but found {Describe(token)}");
        }

        position++;
        return token;
    }

    private string ExpectString() => Expect(IrTokenKind.String, "a string").Text;

    private void ExpectSymbol(string text)
    {
        if (!AcceptSymbol(text))
        {
            throw Fail(Peek, $"expected '{text}' but found {Describe(Peek)}");
        }
    }

    private void ExpectWord(string text)
    {
        if (!AcceptWord(text))
        {
            throw Fail(Peek, $"expected '{text}' but found {Describe(Peek)}");
        }
    }

    private bool AcceptSymbol(string text) => Accept(IsSymbol(Peek, text));

    private bool AcceptWord(string text) => Accept(IsWord(Peek, text));

    private bool Accept(bool matches)
    {
        position += matches ? 1 : 0;
        return matches;
    }

    private static string Describe(IrToken token) => token.Kind == IrTokenKind.End ? "end of input" : $"'{token.Text}'";

    private readonly record struct IrToken(IrTokenKind Kind, string Text, int Line, int Column);
}
