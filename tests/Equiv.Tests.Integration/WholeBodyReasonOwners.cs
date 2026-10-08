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
        // Not a lowering gap: erroneous code is Unknown(Unbound) by design (ADR 0029 decision 2), so it never goes away.
        ["unbound"] = "M3-024",

        // A partial method whose code is in its implementing part, written by a source generator or by hand: the frontend
        // reads the defining declaration (docs/runs/2026-10-07-opaque-tail.md gives the reason to P2-118). Ticket P2-107 makes
        // an unedited one congruent on a same-runtime pair and does not lower it, so `samples/same-runtime-cleanup` has one.
        // An `extern` method has the reason too and keeps it by design (ADR 0054; ticket P2-145): it has no code to lower,
        // so P2-118 will not remove it from `samples/extern-import`.
        ["no-body"] = "P2-118",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}
