using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// One side's terms for a <see cref="CalleeContract"/>: a result or heap leaf's term, the <c>threw</c> flag, the exception
/// type and call trace when the product has them, and the version of each heap map the side leaves.
/// </summary>
internal sealed record ContractSide(Func<PredicateLeaf, Expr?> Leaf, BoolExpr Threw, IntExpr? ExceptionType, Expr? Calls, Func<HeapMap, Expr?> Heap);
