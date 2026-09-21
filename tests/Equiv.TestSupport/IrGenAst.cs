using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.TestSupport;

/// <summary>
/// The small structured language <see cref="IrGen"/> generates and <see cref="IrGenLowering"/>
/// lowers to SSA. Six bv32 slots: parameters <c>a</c> and <c>b</c>, slot 2 (a <c>ref</c>
/// parameter <c>r</c> or a local <c>c</c>), and locals <c>v0</c> to <c>v2</c>. With a heap, a
/// <c>ref</c> map parameter <c>field.Gen.x</c> (bv32 to bv32) is read by <see cref="Load"/> and
/// written by <see cref="Store"/>; without one, a load is its key and a store does nothing.
/// </summary>
internal static class IrGenAst
{
    public const int SlotCount = 6;

    internal abstract record Expr;

    internal sealed record Slot(int Index) : Expr;

    internal sealed record Literal(uint Value) : Expr;

    internal sealed record Binary(IrBinaryOp Op, Expr Left, Expr Right) : Expr;

    internal sealed record Unary(IrUnaryOp Op, Expr Operand) : Expr;

    /// <summary>Reads the heap map at <paramref name="Key"/>.</summary>
    internal sealed record Load(Expr Key) : Expr;

    /// <summary>Truncate to bv8, then sign- or zero-extend back to bv32.</summary>
    internal sealed record Narrow(bool Signed, Expr Operand) : Expr;

    internal abstract record Cond;

    internal sealed record Compare(IrBinaryOp Op, Expr Left, Expr Right) : Cond;

    internal sealed record Logic(IrBinaryOp Op, Cond Left, Cond Right) : Cond;

    internal sealed record Negate(Cond Operand) : Cond;

    internal abstract record Stmt;

    internal sealed record Assign(int Slot, Expr Value) : Stmt;

    /// <summary><c>slot = checked(left op right)</c>: throws System.OverflowException on overflow.</summary>
    internal sealed record Checked(int Slot, IrOverflowOp Op, Expr Left, Expr Right) : Stmt;

    /// <summary>Opaque call; <see cref="MayThrow"/> adds a threw flag and a throw edge.</summary>
    internal sealed record Call(int? Slot, string Callee, ImmutableArray<Expr> Args, bool MayThrow) : Stmt;

    internal sealed record If(Cond Condition, ImmutableArray<Stmt> Then, ImmutableArray<Stmt> Else) : Stmt;

    internal sealed record Switch(Expr Scrutinee, ImmutableArray<(uint Value, ImmutableArray<Stmt> Body)> Cases, ImmutableArray<Stmt> Default) : Stmt;

    /// <summary><c>for (i = 0; i &lt; Count; i++) Body</c>.</summary>
    internal sealed record Loop(int Count, ImmutableArray<Stmt> Body) : Stmt;

    internal sealed record Throw(string ExceptionType) : Stmt;

    /// <summary>Writes <paramref name="Value"/> into the heap map at <paramref name="Key"/>.</summary>
    internal sealed record Store(Expr Key, Expr Value) : Stmt;

    internal sealed record Program(bool HasRef, bool HasHeap, ImmutableArray<uint> Inits, ImmutableArray<Stmt> Body, Expr Result);
}
