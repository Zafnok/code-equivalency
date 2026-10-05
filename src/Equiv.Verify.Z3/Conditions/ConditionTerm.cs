using Equiv.Core.Ir;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Conditions;

/// <summary>
/// A value one body computes from shared inputs alone (ADR 0048 decision 2; ticket P1-022), as a tree that names no
/// variable of either side: its leaves are the product's inputs by index (<see cref="ProductEncoding.Inputs"/>) and
/// constants. The same value on both sides is therefore one term. <see cref="Depth"/> is the tree's height, by which
/// candidates are ordered.
/// </summary>
internal abstract record ConditionTerm(IrType Type, int Depth)
{
    /// <summary>The source parameters the term reads, each as often as it reads it.</summary>
    public IEnumerable<int> Inputs() => this switch
    {
        Input input => [input.Index],
        Null isNull => [isNull.Reference.Index],
        Not not => not.A.Inputs(),
        Unary unary => unary.A.Inputs(),
        Binary binary => [.. binary.A.Inputs(), .. binary.B.Inputs()],
        _ => [],
    };

    /// <summary>The term over <paramref name="encoding"/>'s inputs, built as <see cref="FragmentEncoder"/> builds a body's.</summary>
    public Expr ToExpr(Context context, ProductEncoding encoding) => this switch
    {
        Input input => encoding.Inputs[input.Index].Term,
        Null isNull => context.MkSelect((ArrayExpr)encoding.Inputs[isNull.Map].Term, encoding.Inputs[isNull.Reference.Index].Term),
        Constant constant => encoding.Sorts.Literal(constant.Value),
        Not not => context.MkNot((BoolExpr)not.A.ToExpr(context, encoding)),
        Unary unary => FragmentEncoder.Unary(context, new IrUnary(new IrVar("to", unary.Type), unary.Op, new IrVar("from", unary.A.Type)), unary.A.ToExpr(context, encoding)),
        _ => FragmentEncoder.Binary(context, ((Binary)this).Op, ((Binary)this).A.ToExpr(context, encoding), ((Binary)this).B.ToExpr(context, encoding)),
    };

    /// <summary>The source parameter both sides have at <paramref name="Index"/> of the product's inputs.</summary>
    public sealed record Input(int Index, IrType Type) : ConditionTerm(Type, 0);

    /// <summary>Whether <paramref name="Reference"/> is null: a read of the <c>null.&lt;Sort&gt;</c> input at index <paramref name="Map"/>.</summary>
    public sealed record Null(int Map, Input Reference) : ConditionTerm(new IrBool(), 1);

    /// <summary>A Bool or bitvector constant.</summary>
    public sealed record Constant(IrValue Value) : ConditionTerm(Value.Type, 0);

    /// <summary>The negation of a Bool term.</summary>
    public sealed record Not(ConditionTerm A) : ConditionTerm(A.Type, A.Depth + 1);

    /// <summary>A bitvector <see cref="IrUnary"/> whose result has type <paramref name="Type"/>.</summary>
    public sealed record Unary(IrUnaryOp Op, ConditionTerm A, IrType Type) : ConditionTerm(Type, A.Depth + 1);

    /// <summary>An <see cref="IrBinary"/>; a comparison is Bool, and any other has its operands' type.</summary>
    public sealed record Binary(IrBinaryOp Op, ConditionTerm A, ConditionTerm B) : ConditionTerm(IsComparison(Op) ? new IrBool() : A.Type, Math.Max(A.Depth, B.Depth) + 1);

    /// <summary>Whether <paramref name="op"/> compares its operands: equality, and every signed or unsigned order.</summary>
    public static bool IsComparison(IrBinaryOp op) => op >= IrBinaryOp.Eq;
}
