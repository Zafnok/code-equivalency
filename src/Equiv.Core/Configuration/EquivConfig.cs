using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Configuration;

/// <summary>
/// Parsed and defaulted <c>equiv.config.json</c> (VERIFICATION-MODEL.md section 3; ARCHITECTURE.md's
/// <c>--bound</c>/<c>--timeout-ms</c> CLI defaults). <see cref="CallIdentityRenames"/> maps a legacy-side
/// <c>CallIdentity.Value</c> to its modern-side counterpart so matched calls unify (ticket M3-001).
/// </summary>
public sealed record EquivConfig(RenameMap Renames, ImmutableDictionary<string, string> CallIdentityRenames, int Bound, int TimeoutMs)
{
    /// <summary>The default <see cref="ResourceLimit"/> (ticket P2-050; chosen from <c>docs/runs/2026-10-01-timeout-budget.md</c>).</summary>
    public const int DefaultResourceLimit = 20_000_000;

    public static EquivConfig Default { get; } = new(RenameMap.Empty, [], Bound: 3, TimeoutMs: 5000);

    /// <summary>
    /// <c>resourceLimit</c> (ticket P2-050): Z3's <c>rlimit</c> for each solver query, a count of the solver's own steps, so
    /// the same query gives up at the same point on every machine. <see cref="TimeoutMs"/> is the wall-clock backstop.
    /// </summary>
    public int ResourceLimit { get; init; } = DefaultResourceLimit;

    /// <summary>
    /// Runtime-changed-API member prefixes (ticket M2-006) whose
    /// <see cref="Equiv.Core.RuntimeChanges.RuntimeChangeTable"/> match is suppressed.
    /// </summary>
    public ImmutableArray<string> SuppressRuntimeChanges { get; init; } = [];

    /// <summary>
    /// API-equivalence id prefixes (ticket M3-009, ADR 0020): an
    /// <see cref="Equiv.Core.ApiEquivalences.ApiEquivalenceTable"/> entry whose id starts with one is not applied.
    /// </summary>
    public ImmutableArray<string> SuppressApiEquivalences { get; init; } = [];

    /// <summary>
    /// <c>runtimes.legacy</c> (ADR 0040 decision 1; ticket P2-053): the runtime of a legacy-side <c>netstandard</c> project
    /// that no executable or test project hosts. Null when unset.
    /// </summary>
    public TargetRuntime? LegacyRuntime { get; init; }

    /// <summary><c>runtimes.modern</c>: as <see cref="LegacyRuntime"/>, for the modern side.</summary>
    public TargetRuntime? ModernRuntime { get; init; }

    /// <summary>
    /// <c>--il-fallback</c> (ADR 0039; ticket P1-016): lower a pair that is not congruent and holds an unshared opaque again
    /// from IL on both sides. Off unless the command line sets it; <c>equiv.config.json</c> has no key for it.
    /// </summary>
    public bool IlFallback { get; init; }

    // Deliberate non-short-circuit '&' after the null check, matching Equiv.Core.Ir.IrEquality's
    // documented rationale: '&&' always compiles to a branch per operand, which would need extra
    // tests per field to keep this repo's 100% branch-coverage gate; the operands here are cheap
    // and side-effect-free, and 'other' is already proven non-null by the '&&' above.
    public bool Equals(EquivConfig? other) =>
        other is not null
        && (Renames == other.Renames)
            & ConfigEquality.DictionaryEqual(CallIdentityRenames, other.CallIdentityRenames) // NOSONAR
            & (Bound == other.Bound) // NOSONAR
            & (TimeoutMs == other.TimeoutMs) // NOSONAR
            & IrEquality.SequenceEqual(SuppressRuntimeChanges, other.SuppressRuntimeChanges) // NOSONAR
            & IrEquality.SequenceEqual(SuppressApiEquivalences, other.SuppressApiEquivalences) // NOSONAR
            & (LegacyRuntime == other.LegacyRuntime) // NOSONAR
            & (ModernRuntime == other.ModernRuntime) // NOSONAR
            & (IlFallback == other.IlFallback) // NOSONAR
            & (ResourceLimit == other.ResourceLimit); // NOSONAR

    public override int GetHashCode() =>
        HashCode.Combine(HashCode.Combine(Renames, ConfigEquality.Hash(CallIdentityRenames), Bound, TimeoutMs, IrEquality.Hash(SuppressRuntimeChanges), IrEquality.Hash(SuppressApiEquivalences), LegacyRuntime, ModernRuntime), IlFallback, ResourceLimit);
}
