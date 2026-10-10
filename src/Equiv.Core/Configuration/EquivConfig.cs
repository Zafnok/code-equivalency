using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Configuration;

/// <summary>
/// Parsed and defaulted <c>equiv.config.json</c> (VERIFICATION-MODEL.md sections 3 and 6). <see cref="CallIdentityRenames"/> maps a legacy-side
/// <c>CallIdentity.Value</c> to its modern-side counterpart so matched calls unify (ticket M3-001).
/// </summary>
public sealed record EquivConfig(RenameMap Renames, ImmutableDictionary<string, string> CallIdentityRenames, int Bound, int TimeoutMs)
{
    /// <summary>
    /// The default <see cref="ResourceLimit"/>, the first pass's in either mode (ADR 0049; tickets P2-050 and P1-032): on
    /// the timeout pairs of <c>docs/runs/2026-10-01-timeout-budget.md</c> it gives up 5 answers against 5,000,000 and no proof.
    /// </summary>
    public const int DefaultResourceLimit = 2_000_000;

    /// <summary><see cref="Explicit"/>'s name for <c>bound</c>.</summary>
    public const string BoundSetting = "bound";

    /// <summary><see cref="Explicit"/>'s name for <c>resourceLimit</c>.</summary>
    public const string ResourceLimitSetting = "resourceLimit";

    /// <summary><see cref="Explicit"/>'s name for <c>timeoutMs</c>.</summary>
    public const string TimeoutSetting = "timeoutMs";

    /// <summary><see cref="Explicit"/>'s name for <c>escalation</c>.</summary>
    public const string EscalationSetting = "escalation";

    public static EquivConfig Default { get; } = new(RenameMap.Empty, [], Bound: 3, TimeoutMs: 60_000);

    /// <summary>
    /// <c>resourceLimit</c> (ticket P2-050): Z3's <c>rlimit</c> for each solver query, a count of the solver's own steps, so
    /// the same query gives up at the same point on every machine. <see cref="TimeoutMs"/> is the wall-clock backstop.
    /// </summary>
    public int ResourceLimit { get; init; } = DefaultResourceLimit;

    /// <summary>
    /// The most threads the default <c>jobs</c> uses (ticket P2-132): the largest number measured, on a 24-core machine,
    /// at which a run was no slower than on fewer (<c>docs/runs/2026-10-09-jobs-scaling.md</c>).
    /// </summary>
    public const int MaxDefaultJobs = 24;

    /// <summary>
    /// <c>jobs</c> (tickets P2-077 and P2-132): how many matched pairs are verified at once. The processor count unless
    /// set, and never more than <see cref="MaxDefaultJobs"/>: on <c>gitextensions-8522</c> runs on 4, 8, 12 and 24
    /// threads each gave every result the run on one thread gave, and each was faster than the one before.
    /// </summary>
    public int Jobs { get; init; } = Math.Min(Environment.ProcessorCount, MaxDefaultJobs);

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

    /// <summary>
    /// <c>solvers.cvc5.path</c> (ADR 0050 decision 6; ticket P1-033): the <c>cvc5</c> executable asked the rung 1 queries
    /// Z3 gives up on. Null when unset, and then no second solver is asked.
    /// </summary>
    public string? Cvc5Path { get; init; }

    /// <summary><c>mode</c> (ADR 0049, ADR 0052; ticket P1-032): quick unless set. The command line's <c>--mode</c> wins over it.</summary>
    public CompareMode Mode { get; init; }

    /// <summary>
    /// <c>escalation</c> (ADR 0049 decision 4): the budget pass's bound, resource limit and timeout, ADR 0049's unless set.
    /// The pass never asks with less than the first (<see cref="Configuration.Escalation.AtLeast"/>).
    /// </summary>
    public Escalation Escalation { get; init; } = Escalation.Default;

    /// <summary>
    /// The settings given explicitly, by the file or the command line, among <see cref="BoundSetting"/>,
    /// <see cref="ResourceLimitSetting"/>, <see cref="TimeoutSetting"/> and <see cref="EscalationSetting"/>, in that
    /// order (ADR 0049 decisions 4 and 6): a mode's value applies only where none was given, and the run records which were.
    /// </summary>
    public ImmutableArray<string> Explicit { get; init; } = [];

    /// <summary><see cref="Explicit"/> with <paramref name="setting"/> when <paramref name="given"/>, each name once and in the fixed order.</summary>
    public EquivConfig WithExplicit(string setting, bool given)
    {
        string[] order = [BoundSetting, ResourceLimitSetting, TimeoutSetting, EscalationSetting];
        return given ? this with { Explicit = [.. order.Where(name => Explicit.Contains(name, StringComparer.Ordinal) || string.Equals(name, setting, StringComparison.Ordinal))] } : this;
    }

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
            & (ResourceLimit == other.ResourceLimit) // NOSONAR
            & (Jobs == other.Jobs) // NOSONAR
            & string.Equals(Cvc5Path, other.Cvc5Path, StringComparison.Ordinal) // NOSONAR
            & (Mode == other.Mode) // NOSONAR
            & (Escalation == other.Escalation) // NOSONAR
            & IrEquality.SequenceEqual(Explicit, other.Explicit); // NOSONAR

    public override int GetHashCode() =>
        HashCode.Combine(HashCode.Combine(Renames, ConfigEquality.Hash(CallIdentityRenames), Bound, TimeoutMs, IrEquality.Hash(SuppressRuntimeChanges), IrEquality.Hash(SuppressApiEquivalences), LegacyRuntime, ModernRuntime), IlFallback, ResourceLimit, Cvc5Path, Jobs, Mode, Escalation, IrEquality.Hash(Explicit));
}
