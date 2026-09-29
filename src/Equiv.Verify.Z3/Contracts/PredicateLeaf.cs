using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>A leaf of an <see cref="ObservedPredicate"/>: its kind, the map or input it names (empty for a result) and its type.</summary>
internal sealed record PredicateLeaf(PredicateLeafKind Kind, string Name, IrType Type);
