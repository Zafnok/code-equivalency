using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Equiv.TestSupport.Mutations;

/// <summary>
/// The small C# syntax tree <see cref="PairGen"/> generates, mutates (<see cref="Mutator"/>) and renders (ticket
/// M0-012). A method takes <c>(int a, int b, long c, long d, bool e, string s, int[] u)</c>, starts with the locals
/// <c>int x = a; long y = c; bool z = e;</c>, and lives in the class <c>Oracle</c> beside its static field <c>int F</c>.
/// </summary>
internal static class PairSyntax
{
    public const string ClassName = "Oracle";

    public const string MethodName = "M";

    public const string Field = "F";

    public const string Array = "u";

    public const string Temporary = "t";

    public static string Keyword(Type type) => OracleMethod.Keyword(type);

    /// <summary>The whole compilation unit: the class, its field and the one method.</summary>
    public static string RenderMethod(Method method)
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"public static class {ClassName}\n{{\n    public static int {Field};\n\n")
            .Append(CultureInfo.InvariantCulture, $"    public static {Keyword(method.ReturnType)} {MethodName}(int a, int b, long c, long d, bool e, string s, int[] {Array})\n    {{\n");
        int loops = 0;
        RenderBlock([.. method.Locals, .. method.Body], text, 2, ref loops);
        text.Append("    }\n}\n");
        return text.ToString();
    }

    /// <summary>Renders <paramref name="block"/>; true when it ends in a <c>return</c> or <c>throw</c>, after which nothing is rendered.</summary>
    private static bool RenderBlock(ImmutableArray<IStmt> block, StringBuilder text, int indent, ref int loops)
    {
        string pad = new(' ', indent * 4);
        foreach (IStmt statement in block)
        {
            switch (statement)
            {
                case Declare declare:
                    text.Append(pad).Append(Keyword(declare.Value.Type)).Append(' ').Append(declare.Local).Append(" = ").Append(declare.Value.Render()).Append(";\n");
                    break;
                case Assign assign:
                    text.Append(pad).Append(assign.Target).Append(" = ").Append(assign.Value.Render()).Append(";\n");
                    break;
                case Store store:
                    text.Append(pad).Append(Element.Text(store.Index)).Append(" = ").Append(store.Value.Render()).Append(";\n");
                    break;
                case Return exit:
                    text.Append(pad).Append(exit.Value is null ? "return" : "return ").Append(exit.Value?.Render()).Append(";\n");
                    return true;
                case Throw:
                    text.Append(pad).Append("throw new System.InvalidOperationException();\n");
                    return true;
                case If branch:
                    text.Append(pad).Append("if (").Append(branch.Condition.Render()).Append(")\n");
                    Nested(branch.Then, text, indent, ref loops);
                    text.Append(pad).Append("else\n");
                    Nested(branch.Else, text, indent, ref loops);
                    break;
                case Switch choice:
                    text.Append(pad).Append("switch (").Append(choice.Scrutinee.Render()).Append(")\n").Append(pad).Append("{\n");
                    foreach (Case section in choice.Cases)
                    {
                        text.Append(pad).Append("    case ").Append(section.Label.ToString(CultureInfo.InvariantCulture)).Append(":\n");
                        Section(section.Body, text, indent + 1, ref loops);
                    }

                    text.Append(pad).Append("    default:\n");
                    Section(choice.Default, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n");
                    break;
                case While loop:
                    // The counter bounds the loop, so every generated method terminates.
                    string counter = "k" + (loops++).ToString(CultureInfo.InvariantCulture);
                    text.Append(pad).Append("int ").Append(counter).Append(" = 0;\n")
                        .Append(pad).Append("while (").Append(loop.Condition.Render()).Append(" && ").Append(counter).Append(" < ")
                        .Append(loop.Bound.ToString(CultureInfo.InvariantCulture)).Append(")\n").Append(pad).Append("{\n")
                        .Append(pad).Append("    ").Append(counter).Append("++;\n");
                    RenderBlock(loop.Body, text, indent + 1, ref loops);
                    text.Append(pad).Append("}\n");
                    break;
                case For loop:
                    string index = "i" + (loops++).ToString(CultureInfo.InvariantCulture);
                    text.Append(pad).Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ")
                        .Append(loop.Bound.ToString(CultureInfo.InvariantCulture)).Append("; ").Append(index).Append("++)\n");
                    Nested(loop.Body, text, indent, ref loops);
                    break;
            }
        }

        return false;
    }

    private static void Nested(ImmutableArray<IStmt> block, StringBuilder text, int indent, ref int loops)
    {
        string pad = new(' ', indent * 4);
        text.Append(pad).Append("{\n");
        RenderBlock(block, text, indent + 1, ref loops);
        text.Append(pad).Append("}\n");
    }

    /// <summary>A switch section may not fall through, so one that can reach its end breaks.</summary>
    private static void Section(ImmutableArray<IStmt> block, StringBuilder text, int indent, ref int loops)
    {
        string pad = new(' ', indent * 4);
        text.Append(pad).Append("{\n");
        if (!RenderBlock(block, text, indent + 1, ref loops))
        {
            text.Append(pad).Append("    break;\n");
        }

        text.Append(pad).Append("}\n");
    }

    private static string Context(bool isChecked) => isChecked ? "checked" : "unchecked";

    /// <summary>The whole method: its locals, then its body, which ends in its <c>return</c> (or, when void, a write of <c>F</c>).</summary>
    public sealed record Method(Type ReturnType, ImmutableArray<IStmt> Locals, ImmutableArray<IStmt> Body);

    public interface IExpr
    {
        Type Type { get; }

        /// <summary>False when evaluating it can throw: a division, a checked operation or conversion, or an element read.</summary>
        bool CannotThrow { get; }

        string Render();
    }

    /// <summary>A parameter, a local or the field <c>F</c>.</summary>
    public sealed record Name(Type Type, string Id) : IExpr
    {
        public bool CannotThrow => true;

        public string Render() => Id;
    }

    public sealed record Literal(Type Type, long Value) : IExpr
    {
        public bool CannotThrow => true;

        public string Render() => $"({Value.ToString(CultureInfo.InvariantCulture)}{(Type == typeof(long) ? "L" : string.Empty)})";
    }

    /// <summary>A read of <c>u[Index]</c>, which throws when <c>u</c> is null or too short.</summary>
    public sealed record Element(int Index) : IExpr
    {
        public Type Type => typeof(int);

        public bool CannotThrow => false;

        public string Render() => Text(Index);

        public static string Text(int index) => string.Create(CultureInfo.InvariantCulture, $"{Array}[{index}]");
    }

    /// <summary><c>s == null</c> or <c>s != null</c>.</summary>
    public sealed record NullTest(bool IsNull) : IExpr
    {
        public Type Type => typeof(bool);

        public bool CannotThrow => true;

        public string Render() => IsNull ? "(s == null)" : "(s != null)";
    }

    /// <summary>An arithmetic, bitwise, shift or logical operator; its type is its left operand's.</summary>
    public sealed record Binary(string Op, IExpr Left, IExpr Right, bool IsChecked) : IExpr
    {
        public Type Type => Left.Type;

        public bool CannotThrow => !IsChecked && Op is not ("/" or "%") && Left.CannotThrow && Right.CannotThrow;

        public string Render() => Type == typeof(bool) ? $"({Left.Render()} {Op} {Right.Render()})" : $"{Context(IsChecked)}({Left.Render()} {Op} {Right.Render()})";
    }

    /// <summary><c>-</c> or <c>~</c> on a number, <c>!</c> on a bool.</summary>
    public sealed record Unary(string Op, IExpr Operand, bool IsChecked) : IExpr
    {
        public Type Type => Operand.Type;

        public bool CannotThrow => !IsChecked && Operand.CannotThrow;

        public string Render() => string.Equals(Op, "-", StringComparison.Ordinal) ? $"{Context(IsChecked)}(-{Operand.Render()})" : $"({Op}{Operand.Render()})";
    }

    public sealed record Conversion(Type Type, IExpr Operand, bool IsChecked) : IExpr
    {
        public bool CannotThrow => !IsChecked && Operand.CannotThrow;

        public string Render() => $"{Context(IsChecked)}(({Keyword(Type)}){Operand.Render()})";
    }

    public sealed record Conditional(IExpr Condition, IExpr Then, IExpr Else) : IExpr
    {
        public Type Type => Then.Type;

        public bool CannotThrow => Condition.CannotThrow && Then.CannotThrow && Else.CannotThrow;

        public string Render() => $"({Condition.Render()} ? {Then.Render()} : {Else.Render()})";
    }

    /// <summary>A comparison of two numbers or two bools.</summary>
    public sealed record Relation(string Op, IExpr Left, IExpr Right) : IExpr
    {
        public Type Type => typeof(bool);

        public bool CannotThrow => Left.CannotThrow && Right.CannotThrow;

        public string Render() => $"({Left.Render()} {Op} {Right.Render()})";
    }

    public interface IStmt;

    /// <summary>Declares a local: <c>x</c>, <c>y</c> and <c>z</c> at the top, or the temporary <c>t</c>.</summary>
    public sealed record Declare(string Local, IExpr Value) : IStmt;

    /// <summary>Assigns a local or the field <c>F</c>.</summary>
    public sealed record Assign(string Target, IExpr Value) : IStmt;

    /// <summary><c>u[Index] = Value</c>.</summary>
    public sealed record Store(int Index, IExpr Value) : IStmt;

    public sealed record Return(IExpr? Value) : IStmt;

    /// <summary><c>throw new System.InvalidOperationException();</c>.</summary>
    public sealed record Throw : IStmt;

    public sealed record If(IExpr Condition, ImmutableArray<IStmt> Then, ImmutableArray<IStmt> Else) : IStmt;

    public sealed record Case(int Label, ImmutableArray<IStmt> Body);

    public sealed record Switch(IExpr Scrutinee, ImmutableArray<Case> Cases, ImmutableArray<IStmt> Default) : IStmt;

    /// <summary><c>while (Condition &amp;&amp; k &lt; Bound)</c>, counting <c>k</c> first.</summary>
    public sealed record While(IExpr Condition, ImmutableArray<IStmt> Body, int Bound) : IStmt;

    /// <summary><c>for (int i = 0; i &lt; Bound; i++)</c>.</summary>
    public sealed record For(int Bound, ImmutableArray<IStmt> Body) : IStmt;
}
