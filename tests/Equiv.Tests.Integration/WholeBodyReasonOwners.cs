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
        ["async"] = "M4-006",
        ["lock"] = "M4-011",
        ["catch-filter"] = "M4-008",
        ["Block"] = "M4-008",
        ["no-body"] = "M4-008",

        // Constructors M4-001 left whole-body: one that omits its type's field initializers, a static constructor, and a
        // primary constructor with base arguments (ADR 0029 clarification, 2026-09-25).
        ["field-initializer"] = "M4-008",
        ["ConstructorBodyOperation"] = "M4-008",

        // Not a lowering gap: erroneous code is Unknown(Unbound) by design (ADR 0029 decision 2), so it never goes away.
        ["unbound"] = "M3-024",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}
