using System.Globalization;

using Equiv.Core;

namespace Equiv.Execute;

/// <summary>
/// <c>runtime-diff --member "&lt;CallIdentity prefix or exact&gt;" [--from tfm] [--to tfm] [--seed n] [--cases n] --out report.json</c>
/// (tickets M3-032, P2-056). <see cref="From"/> and <see cref="To"/> are the runtimes the legacy and modern sides run on,
/// <c>net48</c> and <c>net10.0</c> unless given.
/// </summary>
internal sealed record RuntimeDiffOptions(string Member, ulong Seed, int Cases, string Out)
{
    public const string Usage = "usage: runtime-diff --member \"<CallIdentity prefix or exact>\" [--from tfm] [--to tfm] [--seed n] [--cases n] --out report.json";

    private static readonly TargetRuntime DefaultFrom = TargetRuntime.Parse("net48")!;

    private static readonly TargetRuntime DefaultTo = TargetRuntime.Parse("net10.0")!;

    public TargetRuntime From { get; init; } = DefaultFrom;

    public TargetRuntime To { get; init; } = DefaultTo;

    public const int DefaultCases = 64;

    /// <summary>The options <paramref name="args"/> spell, or null with the reason in <paramref name="error"/>.</summary>
    public static RuntimeDiffOptions? Parse(IReadOnlyList<string> args, out string error)
    {
        string? member = null, @out = null;
        TargetRuntime from = DefaultFrom, to = DefaultTo;
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
                case "--from" when TargetRuntime.Parse(value) is { } parsedFrom:
                    from = parsedFrom;
                    break;
                case "--to" when TargetRuntime.Parse(value) is { } parsedTo:
                    to = parsedTo;
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
        return new RuntimeDiffOptions(member, seed, cases, @out) { From = from, To = to };
    }
}
