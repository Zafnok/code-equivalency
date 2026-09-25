namespace Equiv.TestSupport;

/// <summary>
/// A generated C# method over <c>(int a, int b, long c, long d, bool e, string s, int[] u, int[] v, List&lt;int&gt; l)</c> returning
/// <see cref="ReturnType"/> (<c>int</c>, <c>long</c>, <c>bool</c> or <c>void</c>). <see cref="Render"/> gives it a name.
/// </summary>
public sealed record OracleMethod(Type ReturnType, string Body)
{
    public string Render(string name) =>
        $"public static {Keyword(ReturnType)} {name}(int a, int b, long c, long d, bool e, string s, int[] u, int[] v, System.Collections.Generic.List<int> l)\n{{\n    int x = a; long y = c; bool z = e;\n{Body}}}\n";

    internal static string Keyword(Type type) => type switch
    {
        _ when type == typeof(int) => "int",
        _ when type == typeof(long) => "long",
        _ when type == typeof(void) => "void",
        _ => "bool",
    };
}
