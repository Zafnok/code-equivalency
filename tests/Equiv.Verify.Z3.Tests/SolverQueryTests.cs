using Equiv.Core;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// <see cref="SolverQuery"/> and <see cref="SolverModel"/> (ticket P2-100): a query is checked in a context of its own,
/// and its model is read from the context its assertions were written in.
/// </summary>
public sealed class SolverQueryTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>The solver receives each assertion as it was written, in order, a later batch after an earlier one.</summary>
    [Fact]
    public void TheSolverReceivesTheAssertionsItWasGiven()
    {
        using Context context = new();
        BoolExpr p = context.MkBoolConst("p");
        BoolExpr q = context.MkBoolConst("q");
        using Solver written = context.MkSolver();
        written.Add(context.MkAnd(p, q), context.MkOr(p, q), context.MkNot(q), p);

        using SolverQuery query = SolverQuery.Plain(context, Options, context.MkAnd(p, q), context.MkOr(p, q));
        query.Add([context.MkNot(q), p]);

        Assert.Equal(written.ToString(), query.Solver.ToString());
        Assert.Equal(Status.UNSATISFIABLE, query.Check(Options, "written"));
    }

    /// <summary>
    /// A model's values come back as terms of the caller's context: a term built from one can be evaluated again, and a
    /// function's interpretation is read as a map.
    /// </summary>
    [Fact]
    public void AModelIsReadFromTheContextOfTheAssertions()
    {
        using Context context = new();
        using BitVecSort bv32 = context.MkBitVecSort(32);
        FuncDecl f = context.MkFuncDecl("f", bv32, bv32);
        FuncDecl g = context.MkFuncDecl("g", bv32, bv32);
        Expr x = context.MkBVConst("x", 32);
        using SolverQuery query = SolverQuery.Plain(
            context,
            Options,
            context.MkEq(x, context.MkBV(7, 32)),
            context.MkEq(context.MkApp(f, x), context.MkBV(9, 32)),
            context.MkEq(context.MkApp(f, context.MkBV(1, 32)), context.MkBV(2, 32)));

        Assert.Equal(Status.SATISFIABLE, query.Check(Options, "model"));
        SolverModel model = query.Model;
        Expr seven = model.Eval(x, completion: true);
        SolverModel.Interpretation? interpretation = model.Map(f);

        Assert.Equal(context.MkBV(7, 32), seven);
        Assert.Equal(context.MkBV(9, 32), model.Eval(context.MkApp(f, seven), completion: true));
        Assert.NotNull(interpretation);
        Assert.Equal(
            interpretation.Else,
            model.Eval(context.MkApp(f, context.MkBV(1000, 32)), completion: true));
        Assert.All(
            interpretation.Entries,
            entry => Assert.Equal(entry.Value, model.Eval(context.MkApp(f, entry.Key), completion: true)));
        Assert.Null(model.Map(g));
        Assert.Contains("define-fun x", model.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A query that found no model has none to read: Z3's exception, as its solver gives it.</summary>
    [Fact]
    public void AQueryWithoutAModelHasNoneToRead()
    {
        using Context context = new();
        using SolverQuery query = SolverQuery.Plain(context, Options, context.MkFalse());

        Assert.Equal(Status.UNSATISFIABLE, query.Check(Options, "none"));
        Assert.Throws<Z3Exception>(() => query.Model);
    }
}
