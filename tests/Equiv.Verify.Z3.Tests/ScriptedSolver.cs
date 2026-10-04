using System.Collections.Immutable;
using System.Text.RegularExpressions;

using Equiv.Core;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// A second solver for tests (ticket P1-033): it records each script it is asked and answers with
/// <paramref name="answer"/>. <see cref="Solve"/> is an answer that is right, from a Z3 with no resource limit.
/// </summary>
internal sealed partial class ScriptedSolver(Func<string, SmtAnswer> answer) : ISmtSolver
{
    public const string SolverName = "cvc5";

    public const string SolverVersion = "1.4.1";

    public List<string> Scripts { get; } = [];

    public List<TimeSpan> Limits { get; } = [];

    public string Name => SolverName;

    public string Version => SolverVersion;

    public SmtAnswer Ask(string script, TimeSpan limit)
    {
        Scripts.Add(script);
        Limits.Add(limit);
        return answer(script);
    }

    /// <summary>The script's answer from a Z3 with no resource limit, with the value of every constant its <c>get-value</c> names.</summary>
    public static SmtAnswer Solve(string script)
    {
        using Context context = new();
        using Solver solver = Pipeline(context);
        solver.Add(context.ParseSMTLIB2String(script[..script.IndexOf("(check-sat)", StringComparison.Ordinal)]));
        Status status = solver.Check();
        if (status != Status.SATISFIABLE)
        {
            return status == Status.UNSATISFIABLE ? new SmtUnsat() : new SmtUnknown(solver.ReasonUnknown);
        }

        Dictionary<string, Expr> constants = solver.Model.ConstDecls.ToDictionary(static d => d.Name.ToString(), d => solver.Model.ConstInterp(d), StringComparer.Ordinal);
        return new SmtSat(Asked(script).ToImmutableDictionary(
            static name => name,
            name => constants.TryGetValue(name.Trim('|'), out Expr? value) ? Literal(value) : Zero(script, name),
            StringComparer.Ordinal));
    }

    /// <summary>A solver with the backend's tactics (<see cref="Z3Backend.Query"/>) and no resource limit; it gives up after a minute.</summary>
    public static Solver Pipeline(Context context)
    {
        using Tactic solveEqs = context.MkTactic("solve-eqs");
        using Tactic simplify = context.MkTactic("simplify");
        using Tactic propagate = context.MkTactic("propagate-values");
        using Tactic smt = context.MkTactic("smt");
        using Tactic pipeline = context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
        Solver solver = context.MkSolver(pipeline);
        solver.Set(Z3Backend.TimeoutParameter, 60_000u);
        return solver;
    }

    /// <summary><c>sat</c> with every constant the script asks for false or zero: values of the right sorts that need not satisfy it.</summary>
    public static SmtSat Zeros(string script) =>
        new(Asked(script).ToImmutableDictionary(static name => name, name => Zero(script, name), StringComparer.Ordinal));

    /// <summary>The names in the script's <c>get-value</c>, as it writes them.</summary>
    public static ImmutableArray<string> Asked(string script)
    {
        int at = script.IndexOf("(get-value (", StringComparison.Ordinal);
        return at < 0 ? [] : [.. Symbol.Matches(script[(at + "(get-value (".Length)..]).Select(static m => m.Value)];
    }

    /// <summary>A Bool or bit-vector value as an SMT-LIB literal: Z3 prints a bit-vector numeral in decimal.</summary>
    private static string Literal(Expr value) =>
        value is BitVecNum bits ? "#b" + string.Concat(Enumerable.Range(0, (int)bits.SortSize).Select(i => (bits.BigInteger >> ((int)bits.SortSize - 1 - i)).IsEven ? '0' : '1')) : value.ToString();

    private static string Zero(string script, string name)
    {
        Match declared = Regex.Match(script, @"^\(declare-fun\s+" + Regex.Escape(name) + @"\s+\(\)\s+(?<sort>Bool|\(_ BitVec (?<width>\d+)\))\)", RegexOptions.Multiline | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(60));
        return declared.Groups["width"].Success ? "#b" + new string('0', int.Parse(declared.Groups["width"].Value, System.Globalization.CultureInfo.InvariantCulture)) : "false";
    }

    [GeneratedRegex(@"\|[^|]*\||[^\s()]+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex Symbol { get; }
}
