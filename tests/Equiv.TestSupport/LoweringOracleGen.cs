using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CsCheck;

namespace Equiv.TestSupport;

/// <summary>
/// Generators for the lowering oracle (VERIFICATION-MODEL.md section 7, tickets M2-003 and M2-004):
/// straight-line code plus <c>if</c>/<c>else</c>, early returns, compound assignment, <c>++</c>/<c>--</c>
/// and counter-bounded <c>while</c> loops over <c>int</c>/<c>long</c>/<c>bool</c>, and <c>null</c> tests on
/// the reference parameter <c>s</c>, and reads and writes of the class's static <c>int</c> auto-property <c>P</c>
/// (ticket M3-010), and reads and writes of its static <c>int</c> field <c>F</c>, including <c>void</c> methods that end by
/// writing it (ticket M3-007), and reads and writes of the elements of the <c>int[]</c> parameters <c>u</c> and <c>v</c>,
/// which an input may bind to one array (ticket P1-006) or bind <c>v</c> to <c>null</c> (ticket P2-017), and <c>foreach</c> loops
/// that fold each element of the <c>List&lt;int&gt;</c> parameter <c>l</c> into <c>x</c> (ticket M4-001), and <c>decimal</c> arithmetic
/// over the parameter <c>m</c> and <c>int</c> values converted to <c>decimal</c>, converted back to <c>int</c> or compared (ticket
/// M4-002); built as a small AST and
/// rendered to C#. Every expression reads a
/// variable, so none is a compile-time constant (a constant <c>checked</c> overflow or division by zero
/// would be a compile error); literals appear only as right operands, and never as a zero divisor. Every
/// loop counts to a literal bound, so every generated method terminates.
/// </summary>
public static class LoweringOracleGen
{
    private const int Depth = 3;

    private static readonly int[] IntEdges = [0, 1, 2, 3, 7, 31, 32, 33, -1, -2, int.MaxValue, int.MinValue];

    private static readonly long[] LongEdges = [0, 1, 2, 63, 64, -1, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue];

    private static readonly decimal[] DecimalEdges = [0m, 1m, -1m, 0.5m, 2.5m, 12345.678m, 0.0000000000000000000000000001m, decimal.MaxValue, decimal.MinValue];

    private static readonly string[] DecimalArithmetic = ["+", "-", "*", "/", "%"];

    private static readonly string[] Arithmetic = ["+", "-", "*", "/", "%", "&", "|", "^", "<<", ">>"];

    private static readonly string[] Relations = ["<", "<=", ">", ">=", "==", "!="];

    private static readonly string[] Logic = ["&&", "||", "&", "|", "^", "==", "!="];

    private static readonly Type[] Types = [typeof(int), typeof(long), typeof(bool)];

    private static readonly Type[] ReturnTypes = [.. Types, typeof(void)];

    /// <summary>The static <c>int</c> auto-property of the class the generated methods are compiled into.</summary>
    public const string Property = "P";

    /// <summary>The static <c>int</c> field of the class the generated methods are compiled into.</summary>
    public const string Field = "F";

    /// <summary>The <c>int[]</c> parameters, each two elements long; an input may pass one array as both, or <c>v</c> as <c>null</c>.</summary>
    public static readonly ImmutableArray<string> Arrays = ["u", "v"];

    /// <summary>The <c>List&lt;int&gt;</c> parameter a generated <c>foreach</c> enumerates.</summary>
    public const string List = "l";

    public static Gen<OracleMethod> Method { get; } =
        Gen.OneOfConst(ReturnTypes).SelectMany(static type =>
            Gen.Select(Block(type, 2), type == typeof(void) ? FieldValue : ExprGen(type, Depth), (body, result) =>
            {
                StringBuilder text = new();
                int loops = 0;
                RenderBlock(body, text, 1, ref loops);
                // A void method ends by writing the field, so its only observable is the final heap.
                text.Append(type == typeof(void) ? $"    {Field} = " : "    return ").Append(result.Render()).Append(";\n");
                return new OracleMethod(type, text.ToString());
            }));

    public static Gen<OracleInput> Input { get; } =
        Gen.Select(Int, Int, Long, Long, Gen.Bool, Gen.Bool, Gen.Enum<ArrayBinding>(), Gen.OneOfConst(DecimalEdges), static (a, b, c, d, e, s, v, m) => new OracleInput(a, b, c, d, e, s, v, m));

    private static Gen<int> Int => Gen.Frequency((3, Gen.OneOfConst(IntEdges)), (1, Gen.Int[-16, 16]), (1, Gen.Int));

    private static Gen<long> Long => Gen.Frequency((3, Gen.OneOfConst(LongEdges)), (1, Gen.Long[-16, 16]), (1, Gen.Long));

    private static Gen<ImmutableArray<IStmt>> Block(Type returnType, int depth) =>
        StmtGen(returnType, depth).Array[0, 3].Select(static s => ImmutableArray.Create(s));

    private static Gen<IStmt> StmtGen(Type returnType, int depth)
    {
        Gen<IStmt> assign = Gen.OneOfConst(Types).SelectMany(static type =>
            ExprGen(type, Depth).Select(value => (IStmt)new Assign(LocalName(type), value)));
        Gen<IStmt> exit = returnType == typeof(void)
            ? Gen.Const<IStmt>(new Return(Value: null))
            : ExprGen(returnType, Depth).Select(static value => (IStmt)new Return(value));
        Gen<IStmt> update = Gen.OneOfConst(typeof(int), typeof(long)).SelectMany(static type =>
            Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(type == typeof(int) ? typeof(int) : typeof(long), Depth - 1), (op, isChecked, value) =>
                (IStmt)new Compound(LocalName(type), op, ShiftCount(op, value, type), isChecked)));
        Gen<IStmt> property = ExprGen(typeof(int), Depth).Select(static value => (IStmt)new Assign(Property, value));
        Gen<IStmt> field = FieldValue.Select(static value => (IStmt)new Assign(Field, value));
        // Index 2 is past the end of both arrays; a write's value is a field value for the same reason as a field's.
        Gen<IStmt> element = Gen.Select(Gen.OneOfConst([.. Arrays]), Gen.Int[0, 2], FieldValue, static (array, index, value) =>
            (IStmt)new Assign(Element(array, index), value));
        Gen<IStmt> step = Gen.Select(Gen.OneOfConst(typeof(int), typeof(long)), Gen.OneOfConst("++", "--"), Gen.Bool, static (type, op, isChecked) =>
            (IStmt)new Step(LocalName(type), op, isChecked));
        if (depth == 0)
        {
            return Gen.Frequency((4, assign), (1, property), (1, field), (2, element), (2, update), (1, step), (1, exit));
        }

        Gen<IStmt> branch = Gen.Select(ExprGen(typeof(bool), 2), Block(returnType, depth - 1), Block(returnType, depth - 1), static (condition, then, otherwise) =>
            (IStmt)new If(condition, then, otherwise));
        Gen<IStmt> loop = Gen.Select(ExprGen(typeof(bool), 2), Block(returnType, depth - 1), Gen.Int[1, 3], static (condition, body, bound) =>
            (IStmt)new While(condition, body, bound));
        // The element is as often a divisor or shift count, so a loop body also throws out through the `finally`.
        Gen<IStmt> each = Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, Block(returnType, depth - 1), static (op, isChecked, body) =>
            (IStmt)new ForEach(op, isChecked, body));
        return Gen.Frequency((3, assign), (1, property), (1, field), (2, element), (2, update), (1, step), (2, branch), (2, loop), (2, each), (1, exit));
    }

    /// <summary>
    /// A field write's value: one variable and a literal, so it never branches. A value that branches makes the CFG
    /// capture the field reference first, and assigning through that capture is opaque until ticket P2-006.
    /// </summary>
    private static Gen<IExpr> FieldValue =>
        Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(typeof(int), 0), static (op, isChecked, left) => (op, isChecked, left))
            .SelectMany(static t => RightOperand(typeof(int), t.op, literal: true, 1).Select(right => (IExpr)new Binary(t.op, t.left, right, t.isChecked)));

    /// <summary>A shift count is an <c>int</c>; a <c>long</c> target shifted by a <c>long</c> would not compile.</summary>
    private static IExpr ShiftCount(string op, IExpr value, Type type) =>
        op is "<<" or ">>" && type == typeof(long) ? new Conversion(typeof(int), value, IsChecked: false) : value;

    private static string Element(string array, int index) => string.Create(CultureInfo.InvariantCulture, $"{array}[{index}]");

    private static string LocalName(Type type) => type switch
    {
        _ when type == typeof(int) => "x",
        _ when type == typeof(long) => "y",
        _ => "z",
    };

    private static Gen<IExpr> ExprGen(Type type, int depth)
    {
        Gen<IExpr> leaf = Gen.OneOfConst<IExpr>([.. Names(type).Select(static n => new Name(n))]);
        return depth switch
        {
            0 => leaf,
            _ when type == typeof(bool) => Bool(depth, leaf),
            _ => Number(type, depth, leaf),
        };
    }

    private static string[] Names(Type type) => type switch
    {
        _ when type == typeof(int) => ["a", "b", "x", Property, Field, .. Arrays.SelectMany(static a => (string[])[Element(a, 0), Element(a, 1)])],
        _ when type == typeof(long) => ["c", "d", "y"],
        _ => ["e", "z"],
    };

    private static Gen<IExpr> Number(Type type, int depth, Gen<IExpr> leaf)
    {
        Type other = type == typeof(int) ? typeof(long) : typeof(int);
        Gen<IExpr> binary = Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(type, depth - 1), Gen.Bool, static (op, isChecked, left, literal) => (op, isChecked, left, literal))
            .SelectMany(t => RightOperand(type, t.op, t.literal, depth).Select(right => (IExpr)new Binary(t.op, t.left, right, t.isChecked)));
        Gen<IExpr> unary = Gen.Select(Gen.OneOfConst("-", "~"), Gen.Bool, ExprGen(type, depth - 1), static (op, isChecked, operand) => (IExpr)new Unary(op, operand, isChecked));
        Gen<IExpr> conversion = Gen.Select(Gen.Bool, ExprGen(other, depth - 1), (isChecked, operand) => (IExpr)new Conversion(type, operand, isChecked));
        Gen<IExpr> conditional = Gen.Select(ExprGen(typeof(bool), depth - 1), ExprGen(type, depth - 1), ExprGen(type, depth - 1), static (c, t, f) => (IExpr)new Conditional(c, t, f));
        Gen<IExpr> fromDecimal = Gen.Select(Gen.Bool, DecimalGen(depth - 1), (isChecked, operand) => (IExpr)new Conversion(type, operand, isChecked));
        return Gen.Frequency((2, leaf), (5, binary), (1, unary), (1, conversion), (1, conditional), (type == typeof(int) ? 1 : 0, fromDecimal));
    }

    /// <summary>
    /// A <c>decimal</c> expression (ticket M4-002): <c>m</c> or an <c>int</c> converted to <c>decimal</c>, and arithmetic over
    /// them, which can divide by zero or overflow. It has no literal, so it is never a compile-time constant.
    /// </summary>
    private static Gen<IExpr> DecimalGen(int depth)
    {
        Gen<IExpr> leaf = Gen.Frequency(
            (1, Gen.Const<IExpr>(new Name("m"))),
            (1, ExprGen(typeof(int), 0).Select(static operand => (IExpr)new Conversion(typeof(decimal), operand, IsChecked: false))));
        if (depth <= 0)
        {
            return leaf;
        }

        Gen<IExpr> binary = Gen.Select(Gen.OneOfConst(DecimalArithmetic), DecimalGen(depth - 1), DecimalGen(depth - 1), static (op, left, right) => (IExpr)new Binary(op, left, right, IsChecked: false));
        return Gen.Frequency((2, leaf), (3, binary));
    }

    /// <summary>Shift counts are <c>int</c>; a literal divisor is never zero.</summary>
    private static Gen<IExpr> RightOperand(Type type, string op, bool literal, int depth)
    {
        Type operandType = op is "<<" or ">>" ? typeof(int) : type;
        if (!literal)
        {
            return ExprGen(operandType, depth - 1);
        }

        Gen<long> values = operandType == typeof(int) ? Int.Select(static v => (long)v) : Long;
        return values.Where(v => v != 0 || op is not ("/" or "%")).Select(v => (IExpr)new Literal(operandType, v));
    }

    private static Gen<IExpr> Bool(int depth, Gen<IExpr> leaf)
    {
        Gen<IExpr> nullTest = Gen.OneOfConst<IExpr>(new Name("(s == null)"), new Name("(s != null)"));
        Gen<IExpr> relation = Gen.Select(Gen.OneOfConst(Relations), Gen.OneOfConst(typeof(int), typeof(long)), static (op, type) => (op, type))
            .SelectMany(t => Gen.Select(ExprGen(t.type, depth - 1), ExprGen(t.type, depth - 1), (l, r) => (IExpr)new Relation(t.op, l, r)));
        Gen<IExpr> logic = Gen.Select(Gen.OneOfConst(Logic), ExprGen(typeof(bool), depth - 1), ExprGen(typeof(bool), depth - 1), static (op, l, r) => (IExpr)new Relation(op, l, r));
        Gen<IExpr> not = ExprGen(typeof(bool), depth - 1).Select(static operand => (IExpr)new Unary("!", operand, IsChecked: false));
        Gen<IExpr> decimals = Gen.Select(Gen.OneOfConst(Relations), DecimalGen(depth - 1), DecimalGen(depth - 1), static (op, l, r) => (IExpr)new Relation(op, l, r));
        return Gen.Frequency((2, leaf), (3, relation), (2, logic), (1, not), (2, nullTest), (1, decimals));
    }

    private static void RenderBlock(ImmutableArray<IStmt> block, StringBuilder text, int indent, ref int loops)
    {
        string pad = new(' ', indent * 4);
        foreach (IStmt statement in block)
        {
            switch (statement)
            {
                case Assign assign:
                    text.Append(pad).Append(assign.Local).Append(" = ").Append(assign.Value.Render()).Append(";\n");
                    break;
                case Compound update:
                    text.Append(pad).Append(Open(update.IsChecked, pad))
                        .Append(update.Local).Append(' ').Append(update.Op).Append("= ").Append(update.Value.Render()).Append(";\n")
                        .Append(Close(update.IsChecked, pad));
                    break;
                case Step step:
                    text.Append(pad).Append(Open(step.IsChecked, pad)).Append(step.Local).Append(step.Op).Append(";\n").Append(Close(step.IsChecked, pad));
                    break;
                case Return exit:
                    text.Append(pad).Append(exit.Value is null ? "return" : "return ").Append(exit.Value?.Render()).Append(";\n");
                    return; // anything after it would be unreachable
                case If branch:
                    text.Append(pad).Append("if (").Append(branch.Condition.Render()).Append(")\n").Append(pad).Append("{\n");
                    RenderBlock(branch.Then, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n").Append(pad).Append("else\n").Append(pad).Append("{\n");
                    RenderBlock(branch.Else, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n");
                    break;
                case While loop:
                    // The counter bounds the loop, so the generated method always terminates.
                    string counter = "k" + (loops++).ToString(CultureInfo.InvariantCulture);
                    text.Append(pad).Append("int ").Append(counter).Append(" = 0;\n")
                        .Append(pad).Append("while ((").Append(loop.Condition.Render()).Append(") && ").Append(counter)
                        .Append(" < ").Append(loop.Bound.ToString(CultureInfo.InvariantCulture)).Append(")\n").Append(pad).Append("{\n");
                    RenderBlock(loop.Body, text, indent + 1, ref loops);
                    text.Append(pad).Append("    ").Append(counter).Append("++;\n").Append(pad).Append("}\n");
                    break;
                case ForEach each:
                    string item = "w" + (loops++).ToString(CultureInfo.InvariantCulture);
                    string inner = pad + "    ";
                    text.Append(pad).Append("foreach (int ").Append(item).Append($" in {List})\n").Append(pad).Append("{\n")
                        .Append(inner).Append(Open(each.IsChecked, inner)).Append("x ").Append(each.Op).Append("= ").Append(item).Append(";\n")
                        .Append(Close(each.IsChecked, inner));
                    RenderBlock(each.Body, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n");
                    break;
            }
        }
    }

    /// <summary>A statement-level <c>checked</c> needs a block, unlike the expression form.</summary>
    private static string Open(bool isChecked, string pad) => isChecked ? $"checked\n{pad}{{\n{pad}    " : string.Empty;

    private static string Close(bool isChecked, string pad) => isChecked ? $"{pad}}}\n" : string.Empty;

    internal interface IExpr
    {
        string Render();
    }

    internal sealed record Name(string Id) : IExpr
    {
        public string Render() => Id;
    }

    internal sealed record Literal(Type Type, long Value) : IExpr
    {
        public string Render() => $"({Value.ToString(CultureInfo.InvariantCulture)}{(Type == typeof(long) ? "L" : string.Empty)})";
    }

    internal sealed record Binary(string Op, IExpr Left, IExpr Right, bool IsChecked) : IExpr
    {
        public string Render() => $"{Context(IsChecked)}({Left.Render()} {Op} {Right.Render()})";
    }

    internal sealed record Unary(string Op, IExpr Operand, bool IsChecked) : IExpr
    {
        public string Render() => string.Equals(Op, "-", StringComparison.Ordinal) ? $"{Context(IsChecked)}(-{Operand.Render()})" : $"({Op}{Operand.Render()})";
    }

    internal sealed record Conversion(Type Type, IExpr Operand, bool IsChecked) : IExpr
    {
        public string Render() => $"{Context(IsChecked)}(({OracleMethod.Keyword(Type)}){Operand.Render()})";
    }

    internal sealed record Conditional(IExpr Condition, IExpr Then, IExpr Else) : IExpr
    {
        public string Render() => $"({Condition.Render()} ? {Then.Render()} : {Else.Render()})";
    }

    internal sealed record Relation(string Op, IExpr Left, IExpr Right) : IExpr
    {
        public string Render() => $"({Left.Render()} {Op} {Right.Render()})";
    }

    internal interface IStmt;

    internal sealed record Assign(string Local, IExpr Value) : IStmt;

    internal sealed record Compound(string Local, string Op, IExpr Value, bool IsChecked) : IStmt;

    internal sealed record Step(string Local, string Op, bool IsChecked) : IStmt;

    internal sealed record While(IExpr Condition, ImmutableArray<IStmt> Body, int Bound) : IStmt;

    /// <summary><c>foreach (int w in l) { x op= w; Body }</c>.</summary>
    internal sealed record ForEach(string Op, bool IsChecked, ImmutableArray<IStmt> Body) : IStmt;

    internal sealed record Return(IExpr? Value) : IStmt;

    internal sealed record If(IExpr Condition, ImmutableArray<IStmt> Then, ImmutableArray<IStmt> Else) : IStmt;

    private static string Context(bool isChecked) => isChecked ? "checked" : "unchecked";
}
