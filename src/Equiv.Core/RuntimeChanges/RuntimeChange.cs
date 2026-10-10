namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// One row of the runtime-changes table (VERIFICATION-MODEL.md section 3; ADR 0008):
/// <paramref name="Member"/> is a prefix matched against <see cref="CallIdentity.Value"/>
/// (<see cref="Equiv.Core.Matching.ProcedureIdentityNormalizer.Member"/>'s
/// <c>Namespace.Type::Member(ParamType,...)</c> shape), <paramref name="Reason"/> is a one-sentence
/// explanation of the behaviour difference, <paramref name="Url"/> links Microsoft's
/// breaking-change or API documentation for it, and <paramref name="Source"/> says where the row
/// came from (ADR 0035). <see cref="Witness"/> is set only on a <see cref="RuntimeChangeSource.Measured"/>
/// row (ticket M3-033). <see cref="ChangedIn"/> is the first runtime with the new behaviour, or null when that
/// is unknown (ADR 0040 decision 2; ticket P2-054). <see cref="Precondition"/> is set on a row whose change only some
/// arguments can reach (ticket P2-073). <see cref="OrdinalUnaffected"/> is set on a row for a culture-sensitive
/// comparison (ticket P2-075).
/// </summary>
public sealed record RuntimeChange(string Member, string Reason, Uri Url, RuntimeChangeSource Source)
{
    public RuntimeChangeWitness? Witness { get; init; }

    /// <summary>The runtime whose behaviour first differs; <c>netcoreapp1.0</c> is the .NET Framework to .NET boundary, null is unknown.</summary>
    public TargetRuntime? ChangedIn { get; init; }

    /// <summary>What a call's constant arguments must meet for the row to apply to it; null when the row applies to every call.</summary>
    public RuntimeChangePrecondition? Precondition { get; init; }

    /// <summary>True when a call that passes <c>StringComparison.Ordinal</c> or <c>OrdinalIgnoreCase</c> as a constant does not reach the change.</summary>
    public bool OrdinalUnaffected { get; init; }
}
