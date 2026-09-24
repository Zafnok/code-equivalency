using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// A procedure matched by stable identity on both sides (ARCHITECTURE.md). <see cref="Old"/> and
/// <see cref="New"/> always carry an equal <see cref="ProcedureIdentity.Value"/>; both are kept so a
/// reader never has to guess which side a result came from. <see cref="OldBody"/> and
/// <see cref="NewBody"/> are the lowered bodies a frontend attaches (ticket M2-003); null when no
/// frontend lowered the pair. <see cref="TokensEqual"/> is true when a frontend found the two
/// declarations' syntax token sequences equal once trivia is ignored: the census's congruence proxy
/// until bound fingerprints land (ADR 0034; tickets M3-030, M3-015).
/// </summary>
public sealed record ProcedurePair(
    ProcedureIdentity Old,
    ProcedureIdentity New,
    IrProcedure? OldBody = null,
    IrProcedure? NewBody = null,
    bool TokensEqual = false);
