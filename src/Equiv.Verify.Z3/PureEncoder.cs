using System.Collections.Frozen;
using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Pure functions (VERIFICATION-MODEL.md section 5; ADR 0025; ticket M4-002). An <see cref="IrPure"/>'s result is an
/// uninterpreted function of its arguments alone, one per function name and signature, and each exception it can raise a
/// Bool function of the same arguments. Both sides share them, so equal arguments give equal results. A function some
/// application in the pair marks <see cref="IrPure.RuntimeSensitive"/> is side-specific instead: every application of it
/// on either side uses an <c>old.</c> or <c>new.</c> prefixed name, so the two sides are never forced to agree.
/// </summary>
internal sealed class PureEncoder(SortMapper sorts, IEnumerable<IrPure> applications)
{
    private readonly Context context = sorts.Context;
    private readonly FrozenSet<string> sideSpecific = applications.Where(static p => p.RuntimeSensitive).Select(static p => p.Function).ToFrozenSet(StringComparer.Ordinal);
    private readonly Dictionary<string, FuncDecl> functions = new(StringComparer.Ordinal);

    /// <summary>The name <paramref name="pure"/>'s function has on <paramref name="side"/>: its own, or prefixed with the side when side-specific.</summary>
    public string Name(Side side, IrPure pure) =>
        sideSpecific.Contains(pure.Function) ? ProductEncoder.Prefix(side) + "." + pure.Function : pure.Function;

    /// <summary><paramref name="pure"/>'s result and one flag per entry of its <see cref="IrPure.Throws"/>, applied to <paramref name="args"/>.</summary>
    public (Expr Result, ImmutableArray<BoolExpr> Threw) Apply(Side side, IrPure pure, Expr[] args) =>
        (context.MkApp(ResultFunction(side, pure), args), [.. pure.Throws.Select(t => (BoolExpr)context.MkApp(ThrewFunction(side, pure, t.ExceptionType), args))]);

    /// <summary>The result function <c>pure:f(args...)</c> for <paramref name="pure"/>'s function and signature, created on first use.</summary>
    public FuncDecl ResultFunction(Side side, IrPure pure) =>
        Function($"pure:{Name(side, pure)}({Signature(pure)})->{SortMapper.Name(pure.Target.Type)}", pure, sorts.Sort(pure.Target.Type));

    /// <summary>
    /// What makes the tuple functions (<see cref="IrTuple"/>; ticket P2-027) each other's inverse, as ground facts about this
    /// application only: at a <c>tuple.new</c>, each <c>tuple.item</c> of the result is its element; at a <c>tuple.item</c>,
    /// the tuple is <c>tuple.new</c> of its items, so two tuples read to have equal elements are equal. Both are theorems of
    /// tuples, true of every run, so neither is guarded by reachability. Empty for any other function.
    /// </summary>
    public IEnumerable<BoolExpr> Axioms(Side side, IrPure pure, Expr[] args, Expr result)
    {
        if (string.Equals(pure.Function, IrTuple.New, StringComparison.Ordinal))
        {
            return IrTuple.Elements(pure.Target.Type).Select((e, i) => context.MkEq(context.MkApp(Item(side, pure.Target.Type, i + 1, e), result), args[i]));
        }

        if (IrTuple.Position(pure.Function) is null)
        {
            return [];
        }

        IrType tuple = pure.Args[0].Type;
        ImmutableArray<IrType> elements = IrTuple.Elements(tuple);
        IrPure build = new(new IrVar(IrTuple.New, tuple), [], IrTuple.New, [.. elements.Select(static e => new IrVar(IrTuple.New, e))]);
        return [context.MkEq(context.MkApp(ResultFunction(side, build), [.. elements.Select((e, i) => context.MkApp(Item(side, tuple, i + 1, e), args[0]))]), args[0])];
    }

    /// <summary>The <c>tuple.item</c> function reading element <paramref name="position"/>, of type <paramref name="element"/>, of a <paramref name="tuple"/>.</summary>
    private FuncDecl Item(Side side, IrType tuple, int position, IrType element) =>
        ResultFunction(side, new IrPure(new IrVar(IrTuple.Item(position), element), [], IrTuple.Item(position), [new IrVar(IrTuple.New, tuple)]));

    /// <summary>The Bool function saying <paramref name="pure"/>'s function raises <paramref name="exceptionType"/>, created on first use.</summary>
    public FuncDecl ThrewFunction(Side side, IrPure pure, string exceptionType) =>
        Function($"pure.threw:{Name(side, pure)}({Signature(pure)}):{exceptionType}", pure, context.BoolSort);

    private static string Signature(IrPure pure) => string.Join(',', pure.Args.Select(static a => SortMapper.Name(a.Type)));

    private FuncDecl Function(string name, IrPure pure, Sort range)
    {
        if (!functions.TryGetValue(name, out FuncDecl? function))
        {
            function = context.MkFuncDecl(name, [.. pure.Args.Select(a => sorts.Sort(a.Type))], range);
            functions.Add(name, function);
        }

        return function;
    }
}
