namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// Where a <see cref="RuntimeChange"/> row came from (ADR 0035): picked by hand (M2-006), traced to
/// one of Microsoft's compatibility pages (M2-007, <c>docs/runtime-changes-review.md</c>), or measured
/// against both runtimes with a witness input (M3-033).
/// </summary>
public enum RuntimeChangeSource
{
    /// <summary>Picked by hand before the compatibility pages were reviewed.</summary>
    Curated,

    /// <summary>Traced to an entry on Microsoft's compatibility pages.</summary>
    Documented,

    /// <summary>Observed to differ on both runtimes for a witness input.</summary>
    Measured,
}
