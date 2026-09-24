using System.Collections.Immutable;

namespace Equiv.Cli;

/// <summary>
/// The census over changed pairs (ADR 0034): matched pairs that are not congruent. <see cref="ReasonSets"/> maps the
/// sorted, <c>+</c>-joined union of both sides' opaque reasons to its number of changed pairs; <c>""</c> is no opaque.
/// </summary>
internal sealed record ChangedPairCounts(int Pairs, int WithoutOpaque, int WholeBodyOpaque, ImmutableSortedDictionary<string, int> ReasonSets);
