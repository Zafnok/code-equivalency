using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// A procedure matched by stable identity on both sides (ARCHITECTURE.md). <see cref="Old"/> and
/// <see cref="New"/> always carry an equal <see cref="ProcedureIdentity.Value"/>; both are kept so a
/// reader never has to guess which side a result came from. <see cref="OldBody"/> and
/// <see cref="NewBody"/> are the lowered bodies a frontend attaches (ticket M2-003); null when no
/// frontend lowered the pair. <see cref="OldFingerprint"/> and <see cref="NewFingerprint"/> are the bound fingerprints
/// of those bodies (ADR 0024; ticket M3-015); null when a side has no body. <see cref="EquivalencesApplied"/> is the sorted,
/// distinct ids of the API-equivalence catalogue entries that fired while either body was lowered (ADR 0020; ticket M3-009).
/// <see cref="Lowering"/> is the lowering both bodies came from under <c>--il-fallback</c>, <c>operation</c> or <c>il</c>, and
/// <see cref="IlFallbackTried"/> whether the pair was lowered again from IL to choose it (ADR 0039; ticket P1-016); without the
/// flag they are null and false. <see cref="Runtimes"/> is the interval between the runtimes of the two projects the bodies come
/// from, which every runtime rule was applied by (ADR 0040 decision 2; ticket P2-055); null when the frontend knows no runtimes.
/// </summary>
public sealed record ProcedurePair(
    ProcedureIdentity Old,
    ProcedureIdentity New,
    IrProcedure? OldBody = null,
    IrProcedure? NewBody = null)
{
    public ImmutableArray<string> EquivalencesApplied { get; init; } = [];

    public BodyFingerprint? OldFingerprint { get; init; }

    public BodyFingerprint? NewFingerprint { get; init; }

    public string? Lowering { get; init; }

    public bool IlFallbackTried { get; init; }

    public RuntimeInterval? Runtimes { get; init; }
}
