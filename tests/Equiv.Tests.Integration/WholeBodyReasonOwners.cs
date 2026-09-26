using System.Collections.Immutable;

namespace Equiv.Tests.Integration;

/// <summary>
/// ADR 0029 decision 3: every reason that makes a whole body one <c>IrOpaque</c> has a ticket that removes it, until
/// <c>pairsWholeBodyOpaque</c> reaches zero (ticket M3-025 criterion 7). A new whole-body reason fails
/// <see cref="WholeBodyReasonOwnerTests"/> until it gets a row here; the owning ticket drops its rows when it lands.
/// </summary>
internal static class WholeBodyReasonOwners
{
    public static ImmutableSortedDictionary<string, string> Table { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["lock"] = "M4-003",
        // Not a lowering gap: erroneous code is Unknown(Unbound) by design (ADR 0029 decision 2), so it never goes away.
        ["unbound"] = "M3-024",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}
