namespace EgraphSpike;

/// <summary>Hand-written serialisations in <c>BoundSerialiser</c>'s format: each rule closes its case, and no rule reorders an impure or non-integral operand.</summary>
internal static class SelfTest
{
    private const string Head = "Ordinary static=True async=False returns=System.Int32 (None System.Int32, None System.Int32, None System.Boolean)\nBlock syntax=Block implicit=False\n  Return syntax=ReturnStatement implicit=False\n";
    private const string Int = "type=System.Int32";
    private const string Bool = "type=System.Boolean";

    public static int Run()
    {
        (string Name, string A, string B, bool Closes)[] cases =
        [
            ("a+b = b+a", Bin("AddExpression", Int, P(0), P(1)), Bin("AddExpression", Int, P(1), P(0)), true),
            ("(a+b)+a = a+(b+a)", Bin("AddExpression", Int, Bin("AddExpression", Int, P(0), P(1), 1), P(0)), Bin("AddExpression", Int, P(0), Bin("AddExpression", Int, P(1), P(0), 1)), true),
            ("f()+g() != g()+f()", Bin("AddExpression", Int, Call("F"), Call("G")), Bin("AddExpression", Int, Call("G"), Call("F")), false),
            ("s+t != t+s", Bin("AddExpression", "type=System.String", P(0), P(1)), Bin("AddExpression", "type=System.String", P(1), P(0)), false),
            ("checked (a+b)+a != a+(b+a)", Bin("AddExpression", Int, Bin("AddExpression", Int, P(0), P(1), 1, "checked"), P(0), 0, "checked"), Bin("AddExpression", Int, P(0), Bin("AddExpression", Int, P(1), P(0), 1, "checked"), 0, "checked"), false),
            ("a<b = b>a", Bin("LessThanExpression", Bool, P(0), P(1)), Bin("GreaterThanExpression", Bool, P(1), P(0)), true),
            ("a-b = a+(-b)", Bin("SubtractExpression", Int, P(0), P(1)), Bin("AddExpression", Int, P(0), "      Unary syntax=UnaryMinusExpression implicit=False type=System.Int32 context=unchecked\n" + Indent(P(1))), true),
            ("!(a==b) = a!=b", "    Unary syntax=LogicalNotExpression implicit=False type=System.Boolean context=unchecked\n" + Indent(Bin("EqualsExpression", Bool, P(0), P(1))), Bin("NotEqualsExpression", Bool, P(0), P(1)), true),
            ("c ? a : b = !c ? b : a", Cond(P(2, Bool), P(0), P(1)), Cond("      Unary syntax=LogicalNotExpression implicit=False type=System.Boolean context=unchecked\n" + Indent(P(2, Bool)), P(1), P(0)), true),
            ("a==b ? x : y = a!=b ? y : x", Cond(Indent(Bin("EqualsExpression", Bool, P(0), P(1))), P(0), P(1)), Cond(Indent(Bin("NotEqualsExpression", Bool, P(0), P(1))), P(1), P(0)), true),
        ];

        int failures = 0;
        foreach ((string name, string a, string b, bool closes) in cases)
        {
            Outcome outcome = Differ.Close(OpTree.Parse(Head + a), OpTree.Parse(Head + b));
            bool ok = outcome.Closed == closes;
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: closed={outcome.Closed} rules={string.Join(',', outcome.Rules)} residuals={string.Join(';', outcome.Residuals)}");
        }

        return failures == 0 ? 0 : 1;
    }

    private static string P(int ordinal, string type = Int) => $"      ParameterReference syntax=IdentifierName implicit=False {type} symbols=P{ordinal}\n";

    private static string Call(string name) => $"      Invocation syntax=InvocationExpression implicit=False {Int} symbols=N.C::{name}()\n";

    private static string Bin(string syntax, string type, string left, string right, int depth = 0, string context = "unchecked") =>
        Indent($"    Binary syntax={syntax} implicit=False {type} context={context}\n" + left + right, depth);

    private static string Cond(string condition, string whenTrue, string whenFalse) =>
        "    Conditional syntax=ConditionalExpression implicit=False type=System.Int32\n" + condition + whenTrue + whenFalse;

    private static string Indent(string text, int levels = 1) => levels == 0 ? text :
        string.Concat(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => new string(' ', 2 * levels) + line + "\n"));
}
