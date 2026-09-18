namespace Equiv.Core.Matching;

/// <summary>
/// A procedure matched by stable identity on both sides (ARCHITECTURE.md). <see cref="Old"/> and
/// <see cref="New"/> always carry an equal <see cref="ProcedureIdentity.Value"/>; both are kept so a
/// reader never has to guess which side a result came from.
/// </summary>
public sealed record ProcedurePair(ProcedureIdentity Old, ProcedureIdentity New);
