namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// What an adapter item requires of an integer argument (ADR 0020, clarified for ticket P2-142): the argument is passed as
/// a signed integer of <see cref="Bits"/> bits, and the adapter addresses the call only when the frontend knows, at the
/// call, that its value lies in <see cref="Min"/> to <see cref="Max"/>, both included. The range is the part of the
/// legacy and the modern member's domain on which the entry's <see cref="ApiEquivalence.Reason"/> shows they agree; a
/// call whose argument is not known to be inside it is left as it is.
/// </summary>
public sealed record ApiIntegerRange(int Bits, long Min, long Max);
