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

    internal interface IExpr;

    internal sealed record Slot(int Index) : IExpr;

    internal sealed record Literal(uint Value) : IExpr;

    internal sealed record Binary(IrBinaryOp Op, IExpr Left, IExpr Right) : IExpr;

    internal sealed record Unary(IrUnaryOp Op, IExpr Operand) : IExpr;

    /// <summary>Reads the heap map at <paramref name="Key"/>.</summary>
    internal sealed record Load(IExpr Key) : IExpr;

    /// <summary>Truncate to bv8, then sign- or zero-extend back to bv32.</summary>
    internal sealed record Narrow(bool Signed, IExpr Operand) : IExpr;

    internal interface ICond;

    internal sealed record Compare(IrBinaryOp Op, IExpr Left, IExpr Right) : ICond;

    internal sealed record Logic(IrBinaryOp Op, ICond Left, ICond Right) : ICond;

    internal sealed record Negate(ICond Operand) : ICond;

    internal interface IStmt;

    internal sealed record Assign(int Slot, IExpr Value) : IStmt;

    /// <summary><c>slot = checked(left op right)</c>: throws System.OverflowException on overflow.</summary>
    internal sealed record Checked(int Slot, IrOverflowOp Op, IExpr Left, IExpr Right) : IStmt;

    /// <summary>Opaque call; <see cref="MayThrow"/> adds a threw flag and a throw edge.</summary>
    internal sealed record Call(int? Slot, string Callee, ImmutableArray<IExpr> Args, bool MayThrow) : IStmt;

    internal sealed record If(ICond Condition, ImmutableArray<IStmt> Then, ImmutableArray<IStmt> Else) : IStmt;

    internal sealed record Switch(IExpr Scrutinee, ImmutableArray<(uint Value, ImmutableArray<IStmt> Body)> Cases, ImmutableArray<IStmt> Default) : IStmt;

    /// <summary><c>for (i = 0; i &lt; Count; i++) Body</c>.</summary>
    internal sealed record Loop(int Count, ImmutableArray<IStmt> Body) : IStmt;

    internal sealed record Throw(string ExceptionType) : IStmt;

    /// <summary>Writes <paramref name="Value"/> into the heap map at <paramref name="Key"/>.</summary>
    internal sealed record Store(IExpr Key, IExpr Value) : IStmt;

    internal sealed record Program(bool HasRef, bool HasHeap, ImmutableArray<uint> Inits, ImmutableArray<IStmt> Body, IExpr Result);
}
