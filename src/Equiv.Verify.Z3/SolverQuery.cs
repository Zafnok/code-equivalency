using Equiv.Core;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// One solver query, checked in a context of its own (ticket P2-100). Z3 numbers its terms, gives a freed term's number
/// to the next term it builds, and orders terms by number in several places; and the .NET binding frees a term when the
/// garbage collector finalises the last object that wraps it. So in the context an encoder builds its terms in, which
/// terms have which numbers depends on when a collection ran, and the same query spent a different amount of
/// <c>rlimit</c> from run to run. Here the assertions are translated, each batch as one conjunction and so in one pass,
/// into a context that holds nothing else, where the numbers follow from the assertions alone, and nothing in that
/// context is left for a finalizer to free before the query is disposed. The assertions the solver receives are those
/// it was given.
/// <para>
/// The binding cannot translate a model, so <see cref="Model"/> reads it through translation instead
/// (<see cref="SolverModel"/>). <c>Solver.Translate</c> would move a solver in one call, but Z3 5.1 crashes checking a
/// translated tactic solver.
/// </para>
/// </summary>
internal sealed class SolverQuery : IDisposable
{
    private readonly Context home;
    private readonly Context solving = new();
    private readonly List<Expr> asserted = [];
    private Model? found;

    /// <param name="home">The context <paramref name="assertions"/> live in.</param>
    /// <param name="create">Makes the solver in the context it is given, with its limits set.</param>
    /// <param name="assertions">What the solver is to check, at least one.</param>
    public SolverQuery(Context home, Func<Context, Solver> create, IEnumerable<BoolExpr> assertions)
    {
        this.home = home;
        Solver = create(solving);
        Add(assertions);
    }

    /// <summary>The solver, in its own context: for what Z3 received (its assertions) and what it spent (its statistics).</summary>
    public Solver Solver { get; }

    /// <summary>
    /// The model of a satisfiable check, read from the context the assertions live in. With no model, Z3's exception.
    /// </summary>
    public SolverModel Model => new(found ??= Solver.Model, solving, home);

    /// <summary>The solver's reason for answering unknown.</summary>
    public string ReasonUnknown => Solver.ReasonUnknown;

    /// <summary>A query for Z3's default solver, within <paramref name="options"/>' limits.</summary>
    public static SolverQuery Plain(Context home, VerificationOptions options, params BoolExpr[] assertions) =>
        new(
            home,
            solving =>
            {
                Solver solver = solving.MkSolver();
                Z3Backend.Limit(solver, options);
                return solver;
            },
            assertions);

    /// <summary>Adds <paramref name="assertions"/>, at least one, after those the solver already has.</summary>
    public void Add(IEnumerable<BoolExpr> assertions)
    {
        using BoolExpr all = home.MkAnd(assertions);
        Expr moved = all.Translate(solving);
        asserted.Add(moved);
        Solver.Add(moved.Args.Cast<BoolExpr>());
    }

    /// <summary>
    /// Checks the query, as the stage <c>check:</c><paramref name="query"/> (<see cref="Stages"/>). A check still running
    /// after <paramref name="interruptAfterMs"/> (<see cref="Z3Backend.InterruptAfterMs"/> unless a test gives another)
    /// is interrupted (<see cref="Z3Backend.Interruptible{T}(Context, long, Func{T})"/>).
    /// </summary>
    public Status Check(VerificationOptions options, string query, long? interruptAfterMs = null)
    {
        long started = Stages.Start();
        Status status = Z3Backend.Interruptible(solving, interruptAfterMs ?? Z3Backend.InterruptAfterMs(options), () => Solver.Check());
        Stages.Checked(options, query, started, status);
        return status;
    }

    public void Dispose()
    {
        found?.Dispose();
        Solver.Dispose();
        asserted.ForEach(static a => a.Dispose());
        solving.Dispose();
    }
}
