using Equiv.Core.Ir;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// An IR pair from <c>Fixtures/&lt;name&gt;.ir</c>: the first line is <c># &lt;verdict&gt;</c>, further
/// <c>#</c> lines are comments, and a <c>---</c> line separates the old procedure from the new one.
/// </summary>
internal sealed record Fixture(string Name, string Expected, IrProcedure Old, IrProcedure New)
{
    public static Fixture Load(string name)
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".ir"));
        string expected = lines[0].TrimStart('#').Trim();
        string[] body = [.. lines.Where(static l => !l.StartsWith('#'))];
        int separator = Array.IndexOf(body, "---");
        return new Fixture(
            name,
            expected,
            IrText.Parse(string.Join('\n', body[..separator])),
            IrText.Parse(string.Join('\n', body[(separator + 1)..])));
    }

    /// <summary>Parses an inline pair written the same way (no verdict line).</summary>
    public static (IrProcedure Old, IrProcedure New) Pair(string text)
    {
        string[] parts = text.Split("---");
        return (IrText.Parse(parts[0]), IrText.Parse(parts[1]));
    }
}
