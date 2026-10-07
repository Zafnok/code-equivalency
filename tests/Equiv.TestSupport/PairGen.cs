using System.Collections.Immutable;
using System.Threading;

using CsCheck;

using Equiv.Corpus.Seeder;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Equiv.TestSupport.Mutations.PairSyntax;

namespace Equiv.TestSupport;

/// <summary>
/// Generators for the differential soundness gate (VERIFICATION-MODEL.md section 7; ticket M0-012): a C# method and a
/// second one derived from it by one <see cref="MutationOperator"/>, each rendered as the whole class <c>Oracle</c>
/// with its static field <c>int F</c> and the method <c>M(int a, int b, long c, long d, bool e, string s, int[] u)</c>.
/// The constructs are those of <see cref="LoweringOracleGen"/> that IOPERATION-COVERAGE.md marks lowered, narrowed to
/// one field and one array and widened by <c>switch</c>, <c>for</c> and <c>throw</c>: <c>int</c>, <c>long</c> and
/// <c>bool</c> arithmetic, <c>if</c>/<c>else</c>, <c>switch</c> on an <c>int</c>, <c>while</c> and <c>for</c> loops with
/// literal bounds of at most three, <c>throw</c>, and null tests on <c>s</c>. As in <see cref="LoweringOracleGen"/>,
/// every expression reads a variable, so none is a compile-time constant; literals are only right operands, and never a
/// zero divisor. A value written to <c>F</c> or <c>u</c> never branches, because the CFG captures the target of an
/// assignment whose value branches, which is opaque until ticket P2-006. Methods stay under 40 statements. The cleanup
/// operators of ticket P2-048 draw from methods with one more construct (<see cref="WithCleanup"/>), the only place a
/// method has a string local, an <c>if</c> with no <c>else</c>, or a loop bounded by <c>u.Length</c>.
/// </summary>
public static class PairGen
{
    /// <summary>The most statements a generated method has, counting every nested one.</summary>
    public const int MaxStatements = 40;

    private const int Depth = 3;

    private static readonly int[] IntEdges = [0, 1, 2, 3, 7, 31, 32, 33, -1, -2, int.MaxValue, int.MinValue];

    private static readonly long[] LongEdges = [0, 1, 2, 63, 64, -1, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue];

    private static readonly string[] Arithmetic = ["+", "-", "*", "/", "%", "&", "|", "^", "<<", ">>"];

    private static readonly string[] Relations = ["<", "<=", ">", ">=", "==", "!="];

    private static readonly string[] Logic = ["&&", "||", "&", "|", "^"];

    private static readonly Type[] Types = [typeof(int), typeof(long), typeof(bool)];

    private static readonly Type[] ReturnTypes = [.. Types, typeof(void)];

    private static readonly ImmutableArray<IStmt> Locals =
        [new Declare("x", new Name(typeof(int), "a")), new Declare("y", new Name(typeof(long), "c")), new Declare("z", new Name(typeof(bool), "e"))];

    // Declared before Pair, which reads it while the type initialises.
    private static Gen<Method> Method { get; } =
        Gen.OneOfConst(ReturnTypes).SelectMany(static type =>
            Gen.Select(Block(type, 2, 2, 6), type == typeof(void) ? Flat(typeof(int)) : ExprGen(type, Depth, flat: false), (body, result) =>
                new Method(type, Locals, [.. body, type == typeof(void) ? new Assign(Field, result) : new Return(result)])))
        .Where(static m => Count(m.Body) < MaxStatements);

    /// <summary>
    /// A generated method with one more statement, <c>x = ((System.Func&lt;int, int&gt;)(v =&gt; v op e))(e');</c>, somewhere
    /// before its last (ticket M4-004): a lambda the lowerer leaves an opaque fragment, which is one shared call when both
    /// sides have it, so a mutation lands either outside the fragment or inside it. <c>e</c> is a parameter, the field or an
    /// array element, never a local: the <c>RenameLocals</c> operator does not rename inside a lambda.
    /// </summary>
    private static Gen<Method> FragmentMethod { get; } =
        Gen.Select(
            Method,
            Gen.Select(
                Gen.OneOfConst(Arithmetic),
                Gen.Bool,
                Gen.OneOfConst<IExpr>(new Name(typeof(int), "a"), new Name(typeof(int), "b"), new Name(typeof(int), Field), new Element(0)),
                Flat(typeof(int)),
                static (op, isChecked, right, argument) => new Fragment(new Binary(op, new Name(typeof(int), "v"), right, isChecked), argument)),
            Gen.Int[0, MaxStatements],
            static (method, fragment, at) => method with { Body = method.Body.Insert(at % method.Body.Length, new Assign("x", fragment)) });

    /// <summary>
    /// A generated method with one more statement somewhere before its last (ticket P1-017) that assigns a local one of the
    /// constructs ADR 0039's IL fallback exists for, which the IOperation lowering leaves opaque: a lifted <c>int?</c>
    /// operator, a lifted conversion to <c>long?</c>, an interpolated string (one with only <c>string</c> holes, as here,
    /// the IOperation lowering has lowered too since ticket P2-086), or a <c>switch</c> expression on a tuple with
    /// positional patterns. The value branches, so it is never written to <c>F</c> or <c>u</c>, and it holds no cast
    /// outside a nullable one, so the temporary operators never move it.
    /// </summary>
    private static Gen<Method> IlMethod { get; } =
        Gen.Select(
            Method,
            Gen.OneOf(
                Gen.Select(Gen.OneOfConst(Arithmetic), Maybes, Gen.Bool, static (op, maybe, literal) => (op, maybe, literal))
                    .SelectMany(static t => RightOperand(typeof(int), t.op, t.literal, 2, flat: false).Select(right => (IlConstruct)new Lifted(t.op, t.maybe, right))),
                Maybes.Select(static maybe => (IlConstruct)new Widened(maybe)),
                Gen.Select(Gen.Bool, Gen.Bool, static (leading, isNull) => (IlConstruct)new Interpolated(leading, isNull)),
                Gen.Select(ExprGen(typeof(int), 1, flat: false), ExprGen(typeof(bool), 1, flat: false), Gen.Int[-1, 3], ExprGen(typeof(int), 1, flat: false).Array[3], static (scrutinee, flag, label, arms) =>
                    (IlConstruct)new Positional(scrutinee, flag, label, [.. arms]))),
            Gen.Int[0, MaxStatements],
            static (method, construct, at) => method with { Body = method.Body.Insert(at % method.Body.Length, new Assign(LocalName(construct.Type), construct)) });

    /// <summary><c>(c ? (int?)v : null)</c>: an <c>int?</c> whose nullness a run decides.</summary>
    private static Gen<Maybe> Maybes => Gen.Select(ExprGen(typeof(bool), 1, flat: false), ExprGen(typeof(int), 1, flat: false), static (condition, value) => new Maybe(condition, value));

    /// <summary>
    /// A legacy and a modern method, and the operator that derived one from the other. The mutation operators
    /// themselves live in <c>Equiv.Corpus.Seeder</c> (ticket M4-010) and work on Roslyn syntax, so the generated
    /// method is rendered to C# once and reparsed before <see cref="SyntaxMutator"/> sees it.
    /// </summary>
    public static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> Pair { get; } = Pairs(Method);

    /// <summary>As <see cref="Pair"/>, over methods that also call a lambda (ticket M4-004).</summary>
    public static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> FragmentPair { get; } = Pairs(FragmentMethod);

    /// <summary>As <see cref="Pair"/>, over methods that also hold one construct the IL fallback exists for (ticket P1-017).</summary>
    public static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> IlPair { get; } = Pairs(IlMethod);

    /// <summary>
    /// A pair that differs only inside a lambda or only inside a local function (ticket P2-079): a generated method with
    /// one more statement somewhere before its last, <c>F = closure(e);</c>, where the closure is <c>v =&gt; v op K</c> on
    /// the legacy side and <c>v =&gt; v op (K + 1)</c> on the modern one, and <c>op</c> is one under which the two differ
    /// on every <c>v</c>. The closure is a lambda called at once, a local function called by name, or a local function
    /// converted to a delegate and called. Read from IL each is named by an ordinal, the same on both sides, with its
    /// body elsewhere. The pair is in the changing family, as a <see cref="MutationOperator.ChangeConstant"/> of the
    /// closure's literal.
    /// </summary>
    public static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> ClosurePair { get; } =
        Gen.Select(Method, Gen.Int[0, 2], Gen.OneOfConst("+", "-", "^"), Gen.Int[-16, 16], Flat(typeof(int)), Gen.Int[0, MaxStatements], static (method, shape, op, constant, argument, at) =>
            (RenderMethod(WithClosure(method, shape, op, constant, argument, at)), RenderMethod(WithClosure(method, shape, op, constant + 1, argument, at)), MutationOperator.ChangeConstant));

    /// <summary><paramref name="method"/> with <c>F = closure(argument);</c> at <paramref name="at"/>, the closure of <paramref name="shape"/> reading <c>v op constant</c>.</summary>
    private static Method WithClosure(Method method, int shape, string op, int constant, IExpr argument, int at)
    {
        const string Function = "L";
        Binary body = new(op, new Name(typeof(int), "v"), new Literal(typeof(int), constant), IsChecked: false);
        Method declared = shape == 0 ? method : method with { Locals = [.. method.Locals, new LocalFunction(Function, body)] };
        return Insert(declared, at % method.Body.Length, new Assign(Field, shape == 0 ? new Fragment(body, argument) : new Invoke(Function, argument, AsDelegate: shape == 2)));
    }

    /// <summary>
    /// A generated method with one construct a P2-048 cleanup operator rewrites, which the base generator rarely or
    /// never makes: an <c>if</c>/<c>else</c> assigning one local, or returning, from both branches; a trailing <c>if</c>
    /// with no <c>else</c> in a <c>void</c> method; a null check or concatenation over the string local <c>w</c>; or an
    /// index loop over <c>u</c>, whichever <paramref name="op"/> rewrites, so that a site is drawn often enough for
    /// CsCheck's <c>Where</c>. Only the cleanup operators draw from it, so the other operators' pairs are as before.
    /// </summary>
    private static Gen<Method> WithCleanup(Gen<Method> methods, MutationOperator op) => op == MutationOperator.GuardClause
        ? methods.Where(static m => m.ReturnType == typeof(void)).SelectMany(static m => ExprGen(typeof(bool), 2, flat: false).Select(c => m with { Body = [.. m.Body[..^1], new Guard(c, [m.Body[^1]])] }))
        : methods.SelectMany(m => Cleanup(m, op));

    private static Gen<Method> Cleanup(Method m, MutationOperator op)
    {
        Gen<IExpr> condition = ExprGen(typeof(bool), 2, flat: false);
        Gen<int> at = Gen.Int[0, m.Body.Length - 1];
        Name s = new(typeof(string), "s");
        Name w = new(typeof(string), "w");
        Method strings = m with { Locals = [.. m.Locals, new Declare(w.Id, s)] };
        Gen<Method> assigned = Gen.OneOfConst(Types).SelectMany(type => Gen.Select(condition, ExprGen(type, 2, flat: false), ExprGen(type, 2, flat: false), at, (c, p, q, i) =>
            Insert(m, i, new If(c, [new Assign(LocalName(type), p)], [new Assign(LocalName(type), q)]))));
        return op switch
        {
            MutationOperator.IfToConditional when m.ReturnType != typeof(void) => Gen.OneOf(
                assigned,
                Gen.Select(condition, ExprGen(m.ReturnType, Depth, flat: false), (c, q) => m with { Body = [.. m.Body[..^1], new If(c, [m.Body[^1]], [new Return(q)])] })),
            MutationOperator.IfToConditional => assigned,
            MutationOperator.CoalesceNullCheck => Gen.Select(Gen.Bool, at, (isNull, i) =>
                Insert(strings, i, new Assign(w.Id, isNull ? new Conditional(new NullTest(IsNull: true), w, s) : new Conditional(new NullTest(IsNull: false), s, w)))),
            MutationOperator.ConcatToInterpolation => Gen.Select(Gen.Bool, at, (literal, i) => Insert(strings, i, new Assign(w.Id, new Concat(literal ? [new Text("t"), s, w] : [s, w])))),
            _ => Gen.Select(Gen.OneOfConst(Field, "x"), Gen.OneOfConst("+", "^", "|"), at, (target, arithmetic, i) => Insert(m, i, new ArrayLoop(target, arithmetic))),
        };
    }

    private static Method Insert(Method method, int at, IStmt statement) => method with { Body = method.Body.Insert(at, statement) };

    private static readonly float[] SingleEdges =
    [
        0f, -0f, 1f, -1f, 2f, 0.5f, 0.1f, 3f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, -float.Epsilon,
        1.17549421E-38f, float.MaxValue, float.MinValue, 16777216f, 16777218f,
    ];

    private static readonly double[] DoubleEdges =
    [
        0.0, -0.0, 1.0, -1.0, 2.0, 0.5, 0.1, 3.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon,
        2.2250738585072009E-308, double.MaxValue, double.MinValue, 16777217.0, 9007199254740992.0, 9007199254740994.0, 1e300,
    ];

    private static readonly ImmutableArray<IStmt> FloatLocals =
        [.. Locals, new Declare("p", new Name(typeof(double), "h")), new Declare("q", new Name(typeof(float), "g"))];

    /// <summary>
    /// A floating-point pair (ADR 0053; ticket P1-030): a method over the usual parameters and <c>float g, double h</c>,
    /// with the locals <c>double p = h; float q = g;</c>, and a second one that differs from it in one expression, by an
    /// identity of IEEE arithmetic or by a rewrite that is not one. The identities are in the preserving family
    /// (commuted <c>+</c>, <c>*</c> and comparisons, <c>e * 2</c> for <c>e + e</c>, <c>e / 2</c> for <c>e * 0.5</c>, a
    /// double negation, <c>e - f</c> for <c>e + -f</c>, <c>e * 1</c> for <c>e</c>, <c>!(e == f)</c> for <c>e != f</c>);
    /// the others are changing, and several differ only on a NaN, a signed zero or a rounding (<c>!(e &lt; f)</c> for
    /// <c>e &gt;= f</c>, <c>e + 0</c> for <c>e</c>, a regrouped sum). The expression is assigned to a local the method
    /// returns, or decides what it returns. Operands are the parameters, the locals, conversions between the two formats
    /// and from <c>int</c>, and a few literals; multiplication and division are rare, since each costs the solver a
    /// multiplier. The operator names only the family.
    /// </summary>
    public static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> FloatPair { get; } =
        Gen.Select(Gen.OneOfConst(typeof(double), typeof(float)), Gen.Bool, static (real, relational) => (real, relational))
            .SelectMany(static t => Gen.Select(
                t.relational ? RelationSite(t.real) : ArithmeticSite(t.real),
                FloatStatement.Array[0, 3],
                Gen.Bool,
                RealExpr(t.real, 1),
                (site, before, plain, operand) => FloatSources(t.real, t.relational, site, [.. before], plain, operand)));

    /// <summary>An input of a <see cref="FloatPair"/> method: <see cref="Input"/>, with <c>g</c> and <c>h</c> drawn from NaN, both zeros, the infinities, subnormals and the extremes as often as not.</summary>
    public static Gen<PairInput> FloatInput =>
        Gen.Select(
            Input,
            Gen.Frequency((3, Gen.OneOfConst(SingleEdges)), (1, Gen.Single[-8, 8]), (1, Gen.UInt.Select(BitConverter.UInt32BitsToSingle))),
            Gen.Frequency((3, Gen.OneOfConst(DoubleEdges)), (1, Gen.Double[-8, 8]), (1, Gen.ULong.Select(BitConverter.UInt64BitsToDouble))),
            static (input, g, h) => input with { Floats = (g, h) });

    private static (string LegacySource, string ModernSource, MutationOperator Operator) FloatSources(Type real, bool relational, Site site, ImmutableArray<IStmt> before, bool plain, IExpr operand)
    {
        string target = relational ? "z" : LocalName(real);
        Name held = new(relational ? typeof(bool) : real, target);
        Type returned = (relational, plain) switch
        {
            (true, true) => typeof(bool),
            (true, false) => typeof(int),
            _ => real,
        };
        IExpr result = (relational, plain) switch
        {
            (_, true) => held,
            (true, false) => new Conditional(held, new Name(typeof(int), "a"), new Name(typeof(int), "b")),
            _ => new Binary("+", held, operand, IsChecked: false),
        };
        Method legacy = new(returned, FloatLocals, [.. before, new Assign(target, site.Legacy), new Return(result)]) { Floats = true };
        Method modern = legacy with { Body = [.. before, new Assign(target, site.Modern), new Return(result)] };
        return (RenderMethod(legacy), RenderMethod(modern), site.Preserving ? MutationOperator.Commute : MutationOperator.ChangeConstant);
    }

    /// <summary>A statement before the differing one: a new value for <c>p</c> or <c>q</c>, or one chosen by a comparison.</summary>
    private static Gen<IStmt> FloatStatement =>
        Gen.Frequency(
            (2, RealExpr(typeof(double), 2).Select(static v => (IStmt)new Assign("p", v))),
            (2, RealExpr(typeof(float), 2).Select(static v => (IStmt)new Assign("q", v))),
            (1, Gen.Select(Gen.OneOfConst(Relations), RealExpr(typeof(double), 1), RealExpr(typeof(double), 1), RealExpr(typeof(double), 1), RealExpr(typeof(float), 1), static (op, l, r, then, otherwise) =>
                (IStmt)new If(new Relation(op, l, r), [new Assign("p", then)], [new Assign("q", otherwise)]))));

    private static Gen<Site> ArithmeticSite(Type real) =>
        Gen.Select(RealExpr(real, 1), RealExpr(real, 1), RealExpr(real, 1), Gen.Int[0, 13], (e, f, g, kind) => kind switch
        {
            0 => new Site(Op("+", e, f), Op("+", f, e), Preserving: true),
            1 => new Site(Op("*", e, f), Op("*", f, e), Preserving: true),
            2 => new Site(Op("*", e, new Real(real, 2)), Op("+", e, e), Preserving: true),
            3 => new Site(Op("/", e, new Real(real, 2)), Op("*", e, new Real(real, 0.5)), Preserving: true),
            4 => new Site(Negated(Negated(e)), e, Preserving: true),
            5 => new Site(Op("-", e, f), Op("+", e, Negated(f)), Preserving: true),
            6 => new Site(Op("*", e, new Real(real, 1)), e, Preserving: true),
            7 => new Site(Op("+", e, f), Op("-", e, f), Preserving: false),
            8 => new Site(Op("*", e, f), Op("/", e, f), Preserving: false),
            9 => new Site(Op("+", Op("+", e, f), g), Op("+", e, Op("+", f, g)), Preserving: false),
            10 => new Site(Op("+", e, new Real(real, 0)), e, Preserving: false),
            11 => new Site(Op("-", e, e), new Real(real, 0), Preserving: false),
            12 => new Site(Op("*", e, new Real(real, 3)), Op("*", e, new Real(real, 4)), Preserving: false),
            _ => new Site(Op("*", Op("/", e, f), f), e, Preserving: false),
        });

    private static Gen<Site> RelationSite(Type real) =>
        Gen.Select(RealExpr(real, 1), RealExpr(real, 1), Gen.Int[0, 8], static (e, f, kind) => kind switch
        {
            0 => new Site(new Relation("<", e, f), new Relation(">", f, e), Preserving: true),
            1 => new Site(new Relation("<=", e, f), new Relation(">=", f, e), Preserving: true),
            2 => new Site(new Relation("==", e, f), new Relation("==", f, e), Preserving: true),
            3 => new Site(new Relation("!=", e, f), new Relation("!=", f, e), Preserving: true),
            4 => new Site(Not(new Relation("==", e, f)), new Relation("!=", e, f), Preserving: true),
            5 => new Site(new Relation("<", e, f), new Relation("<=", e, f), Preserving: false),
            6 => new Site(Not(new Relation("<", e, f)), new Relation(">=", e, f), Preserving: false),
            7 => new Site(new Relation("<", e, f), Not(new Relation(">=", e, f)), Preserving: false),
            _ => new Site(new Relation("==", e, f), new Relation("<=", e, f), Preserving: false),
        });

    private static Binary Op(string op, IExpr left, IExpr right) => new(op, left, right, IsChecked: false);

    private static Unary Negated(IExpr operand) => new("-", operand, IsChecked: false);

    private static Unary Not(IExpr operand) => new("!", operand, IsChecked: false);

    /// <summary>A <c>float</c> or <c>double</c> expression: a variable, a conversion, a literal, or arithmetic over them.</summary>
    private static Gen<IExpr> RealExpr(Type real, int depth)
    {
        bool wide = real == typeof(double);
        Type other = wide ? typeof(float) : typeof(double);
        Gen<IExpr> leaf = Gen.Frequency(
            (5, Gen.OneOfConst<IExpr>(new Name(real, wide ? "h" : "g"), new Name(real, wide ? "p" : "q"))),
            (1, Gen.OneOfConst<IExpr>(new Name(other, wide ? "g" : "h"), new Name(other, wide ? "q" : "p")).Select(o => (IExpr)new Conversion(real, o, IsChecked: false))),
            (1, Gen.OneOfConst<IExpr>(new Name(typeof(int), "a"), new Name(typeof(int), "x")).Select(o => (IExpr)new Conversion(real, o, IsChecked: false))),
            (2, Gen.OneOfConst(0.0, 1.0, 2.0, 0.5, -1.0, 3.0, 0.1).Select(v => (IExpr)new Real(real, v))));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<IExpr> binary = Gen.Select(
            Gen.Frequency((4, Gen.Const("+")), (4, Gen.Const("-")), (1, Gen.Const("*")), (1, Gen.Const("/"))),
            RealExpr(real, depth - 1),
            RealExpr(real, depth - 1),
            static (op, left, right) => (IExpr)Op(op, left, right));
        return Gen.Frequency((3, leaf), (4, binary), (1, RealExpr(real, depth - 1).Select(static o => (IExpr)Negated(o))));
    }

    /// <summary>The one expression a <see cref="FloatPair"/>'s two methods differ in, and whether the two agree on every input.</summary>
    private sealed record Site(IExpr Legacy, IExpr Modern, bool Preserving);

    private static bool IsCleanup(MutationOperator op) => op is >= MutationOperator.IfToConditional and <= MutationOperator.ForToForeach;

    private static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> Pairs(Gen<Method> methods) =>
        Gen.Enum<MutationOperator>().SelectMany(op =>
            (IsCleanup(op) ? WithCleanup(methods, op) : methods).Select(Rendered).Where(t => SyntaxMutator.Sites(op, t.Method) > 0).SelectMany(t => Mutants(op, t)));

    /// <summary>One mutant of <paramref name="rendered"/> per <paramref name="op"/> site; a named method so the lambdas in <see cref="Pairs"/> bind once (CS9236).</summary>
    private static Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> Mutants(
        MutationOperator op, (string Source, SyntaxNode Root, MethodDeclarationSyntax Method) rendered) =>
        Gen.Int[0, SyntaxMutator.Sites(op, rendered.Method) - 1].Select(site =>
        {
            MethodDeclarationSyntax mutated = SyntaxMutator.Apply(op, rendered.Method, site)!;
            string mutant = rendered.Root.ReplaceNode(rendered.Method, mutated).ToFullString();
            // The introduced temporary is on the legacy side when the operator inlines it.
            return op == MutationOperator.InlineTemporary ? (mutant, rendered.Source, op) : (rendered.Source, mutant, op);
        });

    /// <summary>Renders <paramref name="method"/> and parses it back so <see cref="SyntaxMutator"/> can work on its Roslyn syntax.</summary>
    private static (string Source, SyntaxNode Root, MethodDeclarationSyntax Method) Rendered(Method method)
    {
        string source = RenderMethod(method);
        SyntaxNode root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, cancellationToken: CancellationToken.None).GetRoot(CancellationToken.None);
        MethodDeclarationSyntax declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return (source, root, declaration);
    }

    public static Gen<PairInput> Input { get; } =
        Gen.Select(Int, Int, Long, Long, Gen.Bool, Gen.Bool, Int, Array)
            .Select(static t => new PairInput(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8));

    /// <summary>Whether <paramref name="op"/> is in the preserving family: the two methods behave the same on every input.</summary>
    public static bool IsPreserving(MutationOperator op) => SyntaxMutator.IsPreserving(op);

    private static Gen<int> Int => Gen.Frequency((3, Gen.OneOfConst(IntEdges)), (1, Gen.Int[-16, 16]), (1, Gen.Int));

    private static Gen<long> Long => Gen.Frequency((3, Gen.OneOfConst(LongEdges)), (1, Gen.Long[-16, 16]), (1, Gen.Long));

    /// <summary>Null one time in five, else zero to three elements, so an element read or write can throw either way.</summary>
    private static Gen<ImmutableArray<int>?> Array =>
        Gen.Frequency(
            (1, Gen.Const(default(ImmutableArray<int>?))),
            (4, Int.Array[0, 3].Select(static u => (ImmutableArray<int>?)ImmutableArray.Create(u))));

    private static int Count(ImmutableArray<IStmt> block) => block.Sum(Count);

    /// <summary>The statement and those nested in it; a named method rather than a lambda so each <c>Sum</c> overload binds once (CS9236).</summary>
    private static int Count(IStmt statement) => 1 + statement switch
    {
        If branch => Count(branch.Then) + Count(branch.Else),
        Guard guard => Count(guard.Then),
        Switch choice => choice.Cases.Sum(static c => Count(c.Body)) + Count(choice.Default),
        While loop => Count(loop.Body),
        For loop => Count(loop.Body),
        _ => 0,
    };

    private static Gen<ImmutableArray<IStmt>> Block(Type returnType, int depth, int min, int max) =>
        StmtGen(returnType, depth).Array[min, max].Select(static s => ImmutableArray.Create(s));

    private static Gen<IStmt> StmtGen(Type returnType, int depth)
    {
        // An assignment that cannot throw is what ReorderIndependentStatements swaps, so it is the commonest statement.
        Gen<IStmt> simple = Gen.OneOfConst([.. Types, typeof(void)]).SelectMany(static type => type == typeof(void)
            ? Flat(typeof(int)).Where(static v => v.CannotThrow).Select(static v => (IStmt)new Assign(Field, v))
            : ExprGen(type, 1, flat: false).Where(static v => v.CannotThrow).Select(v => (IStmt)new Assign(LocalName(type), v)));
        Gen<IStmt> assign = Gen.OneOfConst(Types).SelectMany(static type =>
            ExprGen(type, Depth, flat: false).Select(value => (IStmt)new Assign(LocalName(type), value)));
        Gen<IStmt> field = Flat(typeof(int)).Select(static value => (IStmt)new Assign(Field, value));
        // Index 2 is past the end of most arrays an input passes.
        Gen<IStmt> store = Gen.Select(Gen.Int[0, 2], Flat(typeof(int)), static (index, value) => (IStmt)new Store(index, value));
        Gen<IStmt> guard = ExprGen(typeof(bool), 2, flat: false).Select(static condition => (IStmt)new If(condition, [new Throw()], []));
        Gen<IStmt> exit = returnType == typeof(void)
            ? Gen.Const<IStmt>(new Return(Value: null))
            : ExprGen(returnType, Depth, flat: false).Select(static value => (IStmt)new Return(value));
        if (depth == 0)
        {
            return Gen.Frequency((4, simple), (2, assign), (1, field), (2, store), (1, guard), (1, exit));
        }

        Gen<IStmt> nullCheck = Gen.Select(Gen.Bool, Block(returnType, depth - 1, 0, 3), Block(returnType, depth - 1, 0, 3), static (isNull, then, otherwise) =>
            (IStmt)new If(new NullTest(isNull), then, otherwise));
        Gen<IStmt> branch = Gen.Select(ExprGen(typeof(bool), 2, flat: false), Block(returnType, depth - 1, 0, 3), Block(returnType, depth - 1, 0, 3), static (condition, then, otherwise) =>
            (IStmt)new If(condition, then, otherwise));
        Gen<IStmt> choice = Gen.Select(ExprGen(typeof(int), 1, flat: false), Gen.Int[-1, 3].HashSet[1, 3], Block(returnType, depth - 1, 0, 2), static (scrutinee, labels, @default) => (scrutinee, labels, @default))
            .SelectMany(t => Block(returnType, depth - 1, 0, 2).Array[t.labels.Count].Select(bodies =>
                (IStmt)new Switch(t.scrutinee, [.. t.labels.Order().Zip(bodies, static (label, body) => new Case(label, body))], t.@default)));
        Gen<IStmt> loop = Gen.Select(ExprGen(typeof(bool), 2, flat: false), Block(returnType, depth - 1, 1, 3), Gen.Int[1, 3], static (condition, body, bound) =>
            (IStmt)new While(condition, body, bound));
        Gen<IStmt> counted = Gen.Select(Gen.Int[1, 3], Block(returnType, depth - 1, 1, 3), static (bound, body) => (IStmt)new For(bound, body));
        return Gen.Frequency(
            (4, simple), (2, assign), (1, field), (2, store), (2, guard), (1, nullCheck), (2, branch), (1, choice), (1, loop), (1, counted), (1, exit));
    }

    /// <summary>A value that never branches (no <c>?:</c>, <c>&amp;&amp;</c> or <c>||</c>): one variable and a literal.</summary>
    private static Gen<IExpr> Flat(Type type) => ExprGen(type, 2, flat: true);

    private static string LocalName(Type type) => type switch
    {
        _ when type == typeof(int) => "x",
        _ when type == typeof(long) => "y",
        _ when type == typeof(double) => "p",
        _ when type == typeof(float) => "q",
        _ => "z",
    };

    private static Gen<IExpr> ExprGen(Type type, int depth, bool flat)
    {
        Gen<IExpr> leaf = Leaf(type);
        return depth switch
        {
            0 => leaf,
            _ when type == typeof(bool) => Bool(depth, leaf, flat),
            _ => Number(type, depth, leaf, flat),
        };
    }

    private static Gen<IExpr> Leaf(Type type) => type switch
    {
        _ when type == typeof(int) => Gen.Frequency(
            (6, Gen.OneOfConst<IExpr>(new Name(type, "a"), new Name(type, "b"), new Name(type, "x"), new Name(type, Field))),
            (2, Gen.Int[0, 1].Select(static i => (IExpr)new Element(i)))),
        _ when type == typeof(long) => Gen.OneOfConst<IExpr>(new Name(type, "c"), new Name(type, "d"), new Name(type, "y")),
        _ => Gen.OneOfConst<IExpr>(new Name(type, "e"), new Name(type, "z")),
    };

    private static Gen<IExpr> Number(Type type, int depth, Gen<IExpr> leaf, bool flat)
    {
        Type other = type == typeof(int) ? typeof(long) : typeof(int);
        Gen<IExpr> binary = Gen.Select(Gen.OneOfConst(Arithmetic), Gen.Bool, ExprGen(type, depth - 1, flat), Gen.Bool, static (op, isChecked, left, literal) => (op, isChecked, left, literal))
            .SelectMany(t => RightOperand(type, t.op, t.literal, depth, flat).Select(right => (IExpr)new Binary(t.op, t.left, right, t.isChecked)));
        Gen<IExpr> unary = Gen.Select(Gen.OneOfConst("-", "~"), Gen.Bool, ExprGen(type, depth - 1, flat), static (op, isChecked, operand) => (IExpr)new Unary(op, operand, isChecked));
        Gen<IExpr> conversion = Gen.Select(Gen.Bool, ExprGen(other, depth - 1, flat), (isChecked, operand) => (IExpr)new Conversion(type, operand, isChecked));
        if (flat)
        {
            return Gen.Frequency((2, leaf), (5, binary), (1, unary), (1, conversion));
        }

        Gen<IExpr> conditional = Gen.Select(ExprGen(typeof(bool), depth - 1, flat), ExprGen(type, depth - 1, flat), ExprGen(type, depth - 1, flat), static (c, t, f) => (IExpr)new Conditional(c, t, f));
        return Gen.Frequency((2, leaf), (5, binary), (1, unary), (1, conversion), (1, conditional));
    }

    /// <summary>Shift counts are <c>int</c>; a literal divisor is never zero.</summary>
    private static Gen<IExpr> RightOperand(Type type, string op, bool literal, int depth, bool flat)
    {
        Type operandType = op is "<<" or ">>" ? typeof(int) : type;
        if (!literal)
        {
            return ExprGen(operandType, depth - 1, flat);
        }

        Gen<long> values = operandType == typeof(int) ? Int.Select(static v => (long)v) : Long;
        return values.Where(v => v != 0 || op is not ("/" or "%")).Select(v => (IExpr)new Literal(operandType, v));
    }

    /// <summary>
    /// A construct of <see cref="IlMethod"/>, assigned to the local of its <see cref="Type"/>. None is taken to be unable to
    /// throw: each holds a call once lowered from IL, or an operand or arm that may throw.
    /// </summary>
    private abstract record IlConstruct(Type Type) : IExpr
    {
        public bool CannotThrow => false;

        public abstract string Render();
    }

    /// <summary><c>(Condition ? (int?)Value : null)</c>: an <c>int?</c> whose nullness a run decides, made by a nullable conversion.</summary>
    private sealed record Maybe(IExpr Condition, IExpr Value)
    {
        public string Render() => $"({Condition.Render()} ? (int?){Value.Render()} : null)";
    }

    /// <summary><c>unchecked(Operand Op Right).GetValueOrDefault()</c>: a lifted <c>int?</c> operator, null when <see cref="Operand"/> is.</summary>
    private sealed record Lifted(string Op, Maybe Operand, IExpr Right) : IlConstruct(typeof(int))
    {
        public override string Render() => $"unchecked({Operand.Render()} {Op} {Right.Render()}).GetValueOrDefault()";
    }

    /// <summary><c>((long?)Operand).GetValueOrDefault()</c>: a lifted conversion from <c>int?</c> to <c>long?</c>.</summary>
    private sealed record Widened(Maybe Operand) : IlConstruct(typeof(long))
    {
        public override string Render() => $"((long?){Operand.Render()}).GetValueOrDefault()";
    }

    /// <summary><c>($"t{s}" == null)</c> or <c>($"{s}t" != null)</c>: an interpolated string, which C# makes a concatenation.</summary>
    private sealed record Interpolated(bool Leading, bool IsNull) : IlConstruct(typeof(bool))
    {
        public override string Render() => $"({(Leading ? "$\"t{s}\"" : "$\"{s}t\"")} {(IsNull ? "==" : "!=")} null)";
    }

    /// <summary>
    /// <c>((Scrutinee, Flag) switch { (Label, true) =&gt; Arms[0], (_, false) =&gt; Arms[1], _ =&gt; Arms[2] })</c>: positional
    /// patterns on a tuple, none of which subsumes a later one.
    /// </summary>
    private sealed record Positional(IExpr Scrutinee, IExpr Flag, int Label, ImmutableArray<IExpr> Arms) : IlConstruct(typeof(int))
    {
        public override string Render() =>
            $"(({Scrutinee.Render()}, {Flag.Render()}) switch {{ ({Label.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true) => {Arms[0].Render()}, (_, false) => {Arms[1].Render()}, _ => {Arms[2].Render()} }})";
    }

    private static Gen<IExpr> Bool(int depth, Gen<IExpr> leaf, bool flat)
    {
        Gen<IExpr> nullTest = Gen.Bool.Select(static isNull => (IExpr)new NullTest(isNull));
        // Two bools compare only for equality.
        Gen<IExpr> relation = Gen.OneOfConst(Types).SelectMany(static type => Gen.OneOfConst(type == typeof(bool) ? Relations[4..] : Relations).Select(op => (op, type)))
            .SelectMany(t => Gen.Select(ExprGen(t.type, depth - 1, flat), ExprGen(t.type, depth - 1, flat), (l, r) => (IExpr)new Relation(t.op, l, r)));
        Gen<IExpr> logic = Gen.Select(Gen.OneOfConst(flat ? Logic[2..] : Logic), ExprGen(typeof(bool), depth - 1, flat), ExprGen(typeof(bool), depth - 1, flat), static (op, l, r) =>
            (IExpr)new Binary(op, l, r, IsChecked: false));
        Gen<IExpr> not = ExprGen(typeof(bool), depth - 1, flat).Select(static operand => (IExpr)new Unary("!", operand, IsChecked: false));
        return Gen.Frequency((2, leaf), (4, relation), (2, logic), (1, not), (1, nullTest));
    }
}
