using System.Collections.Immutable;

using Equiv.Core.Verdicts;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// A model on which a callee pair breaks a candidate contract: the indices of the conjuncts it falsifies, in order, and
/// the solver's model as text.
/// </summary>
internal sealed record ContractModel(ImmutableArray<int> Falsified, string Text);
