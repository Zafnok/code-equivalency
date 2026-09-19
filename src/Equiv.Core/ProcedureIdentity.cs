namespace Equiv.Core;

/// <summary>
/// The assembly-agnostic identity of a procedure (VERIFICATION-MODEL.md section 4): a normalised
/// member signature or <c>VERB /route</c> for an endpoint, as a single opaque display string.
/// <see cref="Equiv.Core.Matching.ProcedureIdentityNormalizer"/> builds <see cref="Value"/> from raw
/// namespace/type/member/parameter data plus the config's rename maps (ticket M1-003); <c>Equiv.Core.Ir</c>'s
/// text format keeps treating it as an opaque label built from <see cref="Value"/> alone.
/// <see cref="Location"/> is an optional source location a frontend attaches (ticket M2-002, for
/// Added/Removed SARIF results); it does not participate in equality or hashing, so a legacy-side and
/// modern-side <see cref="ProcedureIdentity"/> with the same <see cref="Value"/> still match as the
/// same identity regardless of where each side declares it.
/// </summary>
public sealed record ProcedureIdentity(string Value, SourceSpan? Location = null)
{
    public bool Equals(ProcedureIdentity? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}
