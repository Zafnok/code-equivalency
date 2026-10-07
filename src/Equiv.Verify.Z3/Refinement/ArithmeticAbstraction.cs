using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Refinement;

/// <summary>
/// The hard arithmetic of a product as shared functions (ADR 0025, clarification of 2026-10-07; ticket P1-031). A
/// bitvector multiplication, division or remainder of two unknowns, and the overflow test of such a multiplication, is
/// an uninterpreted function of its two operands, one per operator and width, which both sides share: equal operands
/// give equal results, and nothing else is known of them. Every run of the pair is a run of this product with the
/// functions being the real operators, so a query it cannot satisfy the exact product cannot satisfy either. A model of
/// it may give an application a value the real operator does not (<see cref="Broken"/>). An operator with a constant
/// operand, and every other operator, is never one of these: the solver decides those as they are. Division's zero test
/// and its <c>MinValue / -1</c> test are instructions of their own and stay exact, so no exception is abstracted away.
/// </summary>
internal sealed class ArithmeticAbstraction(Context context)
{
    private readonly Dictionary<string, FuncDecl> functions = new(StringComparer.Ordinal);
    private readonly List<Application> applications = [];

    /// <summary>Every application made so far, both sides', in the order the encoder made them.</summary>
    public IReadOnlyList<Application> Applications => applications;

    /// <summary>Whether <paramref name="op"/> of two unknowns is a shared function.</summary>
    public static bool Abstracts(IrBinaryOp op) => op is IrBinaryOp.Mul or IrBinaryOp.SDiv or IrBinaryOp.SRem or IrBinaryOp.UDiv or IrBinaryOp.URem;

    /// <summary>Whether <paramref name="op"/>, the overflow test of an operator that is one, is a shared function too.</summary>
    public static bool Abstracts(IrOverflowOp op) => op is IrOverflowOp.SMul or IrOverflowOp.UMul;

    /// <summary>Whether <paramref name="procedure"/> holds an operation this abstracts, without which its product is the exact one.</summary>
    public static bool AppliesTo(IrProcedure procedure)
    {
        IrInstruction[] instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        HashSet<string> constants = new(instructions.OfType<IrConst>().Select(static c => c.Target.Name), StringComparer.Ordinal);
        return instructions.Any(i => i switch
        {
            IrBinary binary => Abstracts(binary.Op) && Unknowns(binary.A, binary.B),
            IrOverflows check => Abstracts(check.Op) && Unknowns(check.A, check.B),
            _ => false,
        });

        bool Unknowns(IrVar a, IrVar b) => !constants.Contains(a.Name) && !constants.Contains(b.Name);
    }

    /// <summary><paramref name="op"/> of <paramref name="a"/> and <paramref name="b"/> as its shared function's application.</summary>
    public Expr Binary(IrBinaryOp op, BitVecExpr a, BitVecExpr b) =>
        Apply($"arith.{op}", a, b, a.Sort, (x, y) => ProductEncoder.BitVecOps[op](context, x, y));

    /// <summary>Whether <paramref name="op"/> of <paramref name="a"/> and <paramref name="b"/> overflows, as its shared function's application.</summary>
    public BoolExpr Overflows(IrOverflowOp op, BitVecExpr a, BitVecExpr b) =>
        (BoolExpr)Apply($"arith.overflows.{op}", a, b, context.BoolSort, (x, y) => context.MkNot(ProductEncoder.NoOverflow[op](context, x, y)));

    /// <summary>
    /// What <paramref name="model"/> gets wrong: for each application whose value in it is not the real operator's on the
    /// operands it gives, the fact <c>f(a, b) = a op b</c> with those operands as constants, each fact once, in application
    /// order. The facts are true of the real operators, so a product that assumes them still has every real run. None
    /// means the model is one of the exact product too.
    /// </summary>
    public ImmutableArray<BoolExpr> Broken(SolverModel model)
    {
        Dictionary<string, BoolExpr> facts = new(StringComparer.Ordinal);
        foreach (Application application in applications)
        {
            BitVecExpr a = (BitVecExpr)model.Eval(application.A, completion: true);
            BitVecExpr b = (BitVecExpr)model.Eval(application.B, completion: true);
            BoolExpr fact = context.MkEq(context.MkApp(application.Function, a, b), application.Real(a, b));
            if (!model.Eval(fact, completion: true).IsTrue)
            {
                facts.TryAdd(fact.ToString(), fact);
            }
        }

        return [.. facts.Values];
    }

    private Expr Apply(string op, BitVecExpr a, BitVecExpr b, Sort range, Func<BitVecExpr, BitVecExpr, Expr> real)
    {
        string name = $"{op}.{a.SortSize.ToString(CultureInfo.InvariantCulture)}";
        if (!functions.TryGetValue(name, out FuncDecl? function))
        {
            function = context.MkFuncDecl(name, [a.Sort, b.Sort], range);
            functions.Add(name, function);
        }

        applications.Add(new Application(function, a, b, real));
        return context.MkApp(function, a, b);
    }

    /// <summary>
    /// One application: its function, its operands' terms, and the real operator, which gives the term of its result on
    /// two operands.
    /// </summary>
    internal sealed record Application(FuncDecl Function, BitVecExpr A, BitVecExpr B, Func<BitVecExpr, BitVecExpr, Expr> Real);
}
