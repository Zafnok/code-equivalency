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
}
