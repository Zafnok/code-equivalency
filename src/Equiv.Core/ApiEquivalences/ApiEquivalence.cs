using System.Collections.Immutable;

namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// One row of the API-equivalence catalogue (VERIFICATION-MODEL.md section 3; ADR 0020; ticket M3-009). A member entry
/// (<see cref="IsType"/> false) names a legacy and a modern member as exact <see cref="CallIdentity.Value"/>s and the
/// modern call's <see cref="Arguments"/> in terms of the legacy call's source arguments. A type entry names a legacy and a
/// modern type by metadata name and has no arguments. <see cref="Reason"/> says why the pair meets ADR 0020's soundness
/// condition, and <see cref="Url"/> is the Microsoft Learn page that establishes it.
/// </summary>
public sealed record ApiEquivalence(string Id, bool IsType, string Legacy, string Modern, ImmutableArray<ApiArgument> Arguments, string Reason, Uri Url);
