using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>What a leaf of an <see cref="ObservedPredicate"/> stands for.</summary>
internal enum PredicateLeafKind
{
    /// <summary>The call's result.</summary>
    Result,

    /// <summary>The version of heap map <see cref="PredicateLeaf.Name"/> the call left.</summary>
    Heap,

    /// <summary>The caller's synthesised input <see cref="PredicateLeaf.Name"/>, which no instruction changes (a <c>null.*</c> map).</summary>
    Input,
}
