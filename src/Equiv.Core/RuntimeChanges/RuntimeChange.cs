namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// One row of the runtime-changes table (VERIFICATION-MODEL.md section 3; ADR 0008):
/// <paramref name="Member"/> is a prefix matched against <see cref="CallIdentity.Value"/>
/// (<see cref="Equiv.Core.Matching.ProcedureIdentityNormalizer.Member"/>'s
/// <c>Namespace.Type::Member(ParamType,...)</c> shape), <paramref name="Reason"/> is a one-sentence
/// explanation of the behaviour difference, and <paramref name="Url"/> links Microsoft's
/// breaking-change or API documentation for it.
/// </summary>
public sealed record RuntimeChange(string Member, string Reason, Uri Url);
