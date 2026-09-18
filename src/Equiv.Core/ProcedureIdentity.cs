namespace Equiv.Core;

/// <summary>
/// The assembly-agnostic identity of a procedure (VERIFICATION-MODEL.md section 4): a normalised
/// member signature or <c>VERB /route</c> for an endpoint, as a single opaque display string.
/// <see cref="Equiv.Core.Matching.ProcedureIdentityNormalizer"/> builds <see cref="Value"/> from raw
/// namespace/type/member/parameter data plus the config's rename maps (ticket M1-003); this record
/// itself carries no structure so <c>Equiv.Core.Ir</c>'s text format can keep treating it as an
/// opaque label.
/// </summary>
public sealed record ProcedureIdentity(string Value);
