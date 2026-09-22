using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CsCheck;

namespace Equiv.TestSupport;

/// <summary>
/// Generators for the lowering oracle (VERIFICATION-MODEL.md section 7, tickets M2-003 and M2-004):
/// straight-line code plus <c>if</c>/<c>else</c>, early returns, compound assignment, <c>++</c>/<c>--</c>
/// and counter-bounded <c>while</c> loops over <c>int</c>/<c>long</c>/<c>bool</c>, and <c>null</c> tests on
/// the reference parameter <c>s</c>; built as a small AST and rendered to C#. Every expression reads a
/// variable, so none is a compile-time constant (a constant <c>checked</c> overflow or division by zero
/// would be a compile error); literals appear only as right operands, and never as a zero divisor. Every
/// loop counts to a literal bound, so every generated method terminates.
/// </summary>
public static class LoweringOracleGen
{
    private const int Depth = 3;

    private static readonly int[] IntEdges = [0, 1, 2, 3, 7, 31, 32, 33, -1, -2, int.MaxValue, int.MinValue];

    private static readonly long[] LongEdges = [0, 1, 2, 63, 64, -1, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue];

    private static readonly string[] Arithmetic = ["+", "-", "*", "/", "%", "&", "|", "^", "<<", ">>"];

    private static readonly string[] Relations = ["<", "<=", ">", ">=", "==", "!="];

    private static readonly string[] Logic = ["&&", "||", "&", "|", "^", "==", "!="];

    private static readonly Type[] Types = [typeof(int), typeof(long), typeof(bool)];

    public static Gen<OracleMethod> Method { get; } =
        Gen.OneOfConst(Types).SelectMany(static type =>
            Gen.Select(Block(type, 2), ExprGen(type, Depth), (body, result) =>
            {
                StringBuilder text = new();
                int loops = 0;
                Render(body, text, 1, ref loops);
                text.Append("    return ").Append(result.Render()).Append(";\n");
                return new OracleMethod(type, text.ToString());
            }));

    public static Gen<OracleInput> Input { get; } =
        Gen.Select(Int, Int, Long, Long, Gen.Bool, Gen.Bool, static (a, b, c, d, e, s) => new OracleInput(a, b, c, d, e, s));

    private static Gen<int> Int => Gen.Frequency((3, Gen.OneOfConst(IntEdges)), (1, Gen.Int[-16, 16]), (1, Gen.Int));

    private static Gen<long> Long => Gen.Frequency((3, Gen.OneOfConst(LongEdges)), (1, Gen.Long[-16, 16]), (1, Gen.Long));

    private static Gen<ImmutableArray<Stmt>> Block(Type returnType, int depth) =>
        StmtGen(returnType, depth).Array[0, 3].Select(static s => ImmutableArray.Create(s));

    private static Gen<Stmt> StmtGen(Type returnType, int depth)
    {
        Gen<Stmt> assign = Gen.OneOfConst(Types).SelectMany(static type =>
            ExprGen(type, Depth).Select(value => (Stmt)new Assign(Local(type), value)));
        Gen<Stmt> exit = ExprGen(returnType, Depth).Select(static value => (Stmt)new Return(value));
        Gen<Stmt> update = Gen.OneOfConst(typeof(int), typeof(long)).SelectMany(static type =>
            Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(type == typeof(int) ? typeof(int) : typeof(long), Depth - 1), (op, isChecked, value) =>
                (Stmt)new Compound(Local(type), op, ShiftCount(op, value, type), isChecked)));
        Gen<Stmt> step = Gen.Select(Gen.OneOfConst(typeof(int), typeof(long)), Gen.OneOfConst("++", "--"), Gen.Bool, static (type, op, isChecked) =>
            (Stmt)new Step(Local(type), op, isChecked));
        if (depth == 0)
        {
            return Gen.Frequency((4, assign), (2, update), (1, step), (1, exit));
        }

        Gen<Stmt> branch = Gen.Select(ExprGen(typeof(bool), 2), Block(returnType, depth - 1), Block(returnType, depth - 1), static (condition, then, otherwise) =>
            (Stmt)new If(condition, then, otherwise));
        Gen<Stmt> loop = Gen.Select(ExprGen(typeof(bool), 2), Block(returnType, depth - 1), Gen.Int[1, 3], static (condition, body, bound) =>
            (Stmt)new While(condition, body, bound));
        return Gen.Frequency((3, assign), (2, update), (1, step), (2, branch), (2, loop), (1, exit));
    }

    /// <summary>A shift count is an <c>int</c>; a <c>long</c> target shifted by a <c>long</c> would not compile.</summary>
    private static Expr ShiftCount(string op, Expr value, Type type) =>
        op is "<<" or ">>" && type == typeof(long) ? new Conversion(typeof(int), value, IsChecked: false) : value;

    private static string Local(Type type) => type == typeof(int) ? "x" : type == typeof(long) ? "y" : "z";

    private static Gen<Expr> ExprGen(Type type, int depth)
    {
        Gen<Expr> leaf = Gen.OneOfConst<Expr>([.. Names(type).Select(static n => new Name(n))]);
        return depth == 0 ? leaf : type == typeof(bool) ? Bool(depth, leaf) : Number(type, depth, leaf);
    }

    private static string[] Names(Type type) => type == typeof(int) ? ["a", "b", "x"] : type == typeof(long) ? ["c", "d", "y"] : ["e", "z"];

    private static Gen<Expr> Number(Type type, int depth, Gen<Expr> leaf)
    {
        Type other = type == typeof(int) ? typeof(long) : typeof(int);
        Gen<Expr> binary = Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(type, depth - 1), Gen.Bool, static (op, isChecked, left, literal) => (op, isChecked, left, literal))
            .SelectMany(t => RightOperand(type, t.op, t.literal, depth).Select(right => (Expr)new Binary(t.op, t.left, right, t.isChecked)));
        Gen<Expr> unary = Gen.Select(Gen.OneOfConst("-", "~"), Gen.Bool, ExprGen(type, depth - 1), static (op, isChecked, operand) => (Expr)new Unary(op, operand, isChecked));
        Gen<Expr> conversion = Gen.Select(Gen.Bool, ExprGen(other, depth - 1), (isChecked, operand) => (Expr)new Conversion(type, operand, isChecked));
        Gen<Expr> conditional = Gen.Select(ExprGen(typeof(bool), depth - 1), ExprGen(type, depth - 1), ExprGen(type, depth - 1), static (c, t, f) => (Expr)new Conditional(c, t, f));
        return Gen.Frequency((2, leaf), (5, binary), (1, unary), (1, conversion), (1, conditional));
    }

    /// <summary>Shift counts are <c>int</c>; a literal divisor is never zero.</summary>
    private static Gen<Expr> RightOperand(Type type, string op, bool literal, int depth)
    {
        Type operandType = op is "<<" or ">>" ? typeof(int) : type;
        if (!literal)
        {
            return ExprGen(operandType, depth - 1);
        }

        Gen<long> values = operandType == typeof(int) ? Int.Select(static v => (long)v) : Long;
        return values.Where(v => v != 0 || op is not ("/" or "%")).Select(v => (Expr)new Literal(operandType, v));
    }

    private static Gen<Expr> Bool(int depth, Gen<Expr> leaf)
    {
        Gen<Expr> nullTest = Gen.OneOfConst<Expr>(new Name("(s == null)"), new Name("(s != null)"));
        Gen<Expr> relation = Gen.Select(Gen.OneOfConst(Relations), Gen.OneOfConst(typeof(int), typeof(long)), static (op, type) => (op, type))
            .SelectMany(t => Gen.Select(ExprGen(t.type, depth - 1), ExprGen(t.type, depth - 1), (l, r) => (Expr)new Relation(t.op, l, r)));
        Gen<Expr> logic = Gen.Select(Gen.OneOfConst(Logic), ExprGen(typeof(bool), depth - 1), ExprGen(typeof(bool), depth - 1), static (op, l, r) => (Expr)new Relation(op, l, r));
        Gen<Expr> not = ExprGen(typeof(bool), depth - 1).Select(static operand => (Expr)new Unary("!", operand, IsChecked: false));
        return Gen.Frequency((2, leaf), (3, relation), (2, logic), (1, not), (2, nullTest));
    }

    private static void Render(ImmutableArray<Stmt> block, StringBuilder text, int indent, ref int loops)
    {
        string pad = new(' ', indent * 4);
        foreach (Stmt statement in block)
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
                    text.Append(pad).Append("return ").Append(exit.Value.Render()).Append(";\n");
                    return; // anything after it would be unreachable
                case If branch:
                    text.Append(pad).Append("if (").Append(branch.Condition.Render()).Append(")\n").Append(pad).Append("{\n");
                    Render(branch.Then, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n").Append(pad).Append("else\n").Append(pad).Append("{\n");
                    Render(branch.Else, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n");
                    break;
                case While loop:
                    // The counter bounds the loop, so the generated method always terminates.
                    string counter = "k" + (loops++).ToString(CultureInfo.InvariantCulture);
                    text.Append(pad).Append("int ").Append(counter).Append(" = 0;\n")
                        .Append(pad).Append("while ((").Append(loop.Condition.Render()).Append(") && ").Append(counter)
                        .Append(" < ").Append(loop.Bound.ToString(CultureInfo.InvariantCulture)).Append(")\n").Append(pad).Append("{\n");
                    Render(loop.Body, text, indent + 1, ref loops);
                    text.Append(pad).Append("    ").Append(counter).Append("++;\n").Append(pad).Append("}\n");
                    break;
            }
        }
    }

    /// <summary>A statement-level <c>checked</c> needs a block, unlike the expression form.</summary>
    private static string Open(bool isChecked, string pad) => isChecked ? $"checked\n{pad}{{\n{pad}    " : string.Empty;

    private static string Close(bool isChecked, string pad) => isChecked ? $"{pad}}}\n" : string.Empty;

    internal abstract record Expr
    {
        public abstract string Render();
    }

    internal sealed record Name(string Id) : Expr
    {
        public override string Render() => Id;
    }

    internal sealed record Literal(Type Type, long Value) : Expr
    {
        public override string Render() => $"({Value.ToString(CultureInfo.InvariantCulture)}{(Type == typeof(long) ? "L" : string.Empty)})";
    }

    internal sealed record Binary(string Op, Expr Left, Expr Right, bool IsChecked) : Expr
    {
        public override string Render() => $"{Context(IsChecked)}({Left.Render()} {Op} {Right.Render()})";
    }

    internal sealed record Unary(string Op, Expr Operand, bool IsChecked) : Expr
    {
        public override string Render() => string.Equals(Op, "-", StringComparison.Ordinal) ? $"{Context(IsChecked)}(-{Operand.Render()})" : $"({Op}{Operand.Render()})";
    }

    internal sealed record Conversion(Type Type, Expr Operand, bool IsChecked) : Expr
    {
        public override string Render() => $"{Context(IsChecked)}(({OracleMethod.Keyword(Type)}){Operand.Render()})";
    }

    internal sealed record Conditional(Expr Condition, Expr Then, Expr Else) : Expr
    {
        public override string Render() => $"({Condition.Render()} ? {Then.Render()} : {Else.Render()})";
    }

    internal sealed record Relation(string Op, Expr Left, Expr Right) : Expr
    {
        public override string Render() => $"({Left.Render()} {Op} {Right.Render()})";
    }

    internal abstract record Stmt;

    internal sealed record Assign(string Local, Expr Value) : Stmt;

    internal sealed record Compound(string Local, string Op, Expr Value, bool IsChecked) : Stmt;

    internal sealed record Step(string Local, string Op, bool IsChecked) : Stmt;

    internal sealed record While(Expr Condition, ImmutableArray<Stmt> Body, int Bound) : Stmt;

    internal sealed record Return(Expr Value) : Stmt;

    internal sealed record If(Expr Condition, ImmutableArray<Stmt> Then, ImmutableArray<Stmt> Else) : Stmt;

    private static string Context(bool isChecked) => isChecked ? "checked" : "unchecked";
}
