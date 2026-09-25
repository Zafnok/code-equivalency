namespace Equiv.Core.Matching;

/// <summary>
/// A lowered body's bound fingerprint (ADR 0024): <see cref="Sha256Hex"/> is the lower-case hex SHA-256 of the canonical
/// serialisation of the body's bound tree, and <see cref="RuntimeSensitive"/> says whether that tree can behave differently
/// between .NET Framework and .NET even when it is identical. A matched pair whose two fingerprints are equal and not
/// runtime-sensitive is Equivalent by congruence (ticket M3-015).
/// </summary>
public sealed record BodyFingerprint(string Sha256Hex, bool RuntimeSensitive);
