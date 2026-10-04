using System.Globalization;

using Equiv.Core;

namespace Equiv.Verify.Cvc5;

/// <summary>
/// <see cref="ISmtSolver"/> over the <c>cvc5</c> executable, run as a process once per script (ADR 0050 decisions 1, 5
/// and 6; ticket P1-033). cvc5 is never linked and never shipped: its release binary links LGPL libraries (ADR 0017), so
/// <c>equiv</c> runs the executable <c>equiv.config.json</c> names (<c>solvers.cvc5.path</c>). Each script gets
/// <c>--arrays-exp</c>, which reads the constant arrays of the product encoding, <c>--rlimit</c>
/// (<see cref="ResourceLimit"/>), cvc5's own count of its steps, so the same script gives up at the same point on any
/// machine, and <c>--tlimit</c>, the wall-clock limit behind it. A process still running a little after that limit is
/// killed, and the script is unknown.
/// </summary>
public sealed class Cvc5Solver : ISmtSolver
{
    /// <summary>
    /// The <c>--rlimit</c> of each script, chosen from <c>docs/runs/2026-10-04-cvc5-budget.md</c> (ticket P1-033
    /// criterion 7).
    /// </summary>
    public const long DefaultResourceLimit = 2_000_000;

    /// <summary>How long past <c>--tlimit</c> the process may run before it is killed: cvc5 checks its own limit between steps.</summary>
    internal static readonly TimeSpan KillAfter = TimeSpan.FromSeconds(5);

    private readonly string path;
    private readonly Cvc5Process.Runner run;
    private readonly Lazy<string> version;

    public Cvc5Solver(string path)
        : this(path, Cvc5Process.Run)
    {
    }

    /// <summary>For tests: <paramref name="run"/> stands in for the process.</summary>
    internal Cvc5Solver(string path, Cvc5Process.Runner run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        this.run = run;
        version = new Lazy<string>(() => SmtOutput.Version(run(path, ["--version"], script: null, KillAfter)));
    }

    public string Name => "cvc5";

    /// <summary>The version <c>cvc5 --version</c> prints, read once; <c>unknown</c> when it prints none.</summary>
    public string Version => version.Value;

    /// <summary>The <c>--rlimit</c> each script gets.</summary>
    public long ResourceLimit { get; init; } = DefaultResourceLimit;

    public SmtAnswer Ask(string script, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(script);
        string[] arguments =
        [
            "--arrays-exp",
            "--rlimit=" + ResourceLimit.ToString(CultureInfo.InvariantCulture),
            "--tlimit=" + ((long)limit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
        ];
        return run(path, arguments, script, limit + KillAfter) is { } output ? SmtOutput.Parse(output) : new SmtUnknown("wall-clock limit: the process was killed");
    }
}
