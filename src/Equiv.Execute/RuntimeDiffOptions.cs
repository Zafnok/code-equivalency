using System.Globalization;

namespace Equiv.Execute;

/// <summary><c>runtime-diff --member "&lt;CallIdentity prefix or exact&gt;" [--seed n] [--cases n] --out report.json</c> (ticket M3-032).</summary>
internal sealed record RuntimeDiffOptions(string Member, ulong Seed, int Cases, string Out)
{
    public const string Usage = "usage: runtime-diff --member \"<CallIdentity prefix or exact>\" [--seed n] [--cases n] --out report.json";

    public const int DefaultCases = 64;

    /// <summary>The options <paramref name="args"/> spell, or null with the reason in <paramref name="error"/>.</summary>
    public static RuntimeDiffOptions? Parse(IReadOnlyList<string> args, out string error)
    {
        string? member = null, @out = null;
        ulong seed = 0;
        int cases = DefaultCases;
        for (int i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                error = $"{args[i]} needs a value";
                return null;
            }

            string value = args[i + 1];
            switch (args[i])
            {
                case "--member":
                    member = value;
                    break;
                case "--out":
                    @out = value;
                    break;
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedSeed):
                    seed = parsedSeed;
                    break;
                case "--cases" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedCases) && parsedCases > 0:
                    cases = parsedCases;
                    break;
                default:
                    error = $"unexpected {args[i]} {value}";
                    return null;
            }
        }

        if (member is null || @out is null)
        {
            error = "--member and --out are required";
            return null;
        }

        if (!member.Contains("::", StringComparison.Ordinal))
        {
            error = "--member must name a type and a member, as in System.String::IndexOf(";
            return null;
        }

        error = string.Empty;
        return new RuntimeDiffOptions(member, seed, cases, @out);
    }
}
