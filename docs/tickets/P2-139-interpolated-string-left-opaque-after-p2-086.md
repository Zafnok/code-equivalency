# P2-139 The interpolated strings P2-086 left opaque are counted, and the largest form without a ticket lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-086 lowers an interpolated string whose holes are `string` or 8- to 64-bit integers with no
format or alignment clause, and is done. Every other interpolated string is an opaque node with
reason `InterpolatedString`. P2-102 owns one of the forms left, an integer hole before a hole that
runs code. Nobody has counted the others. P1-028's row (`docs/runs/2026-10-07-opaque-tail.md`),
over the three large runs' 2,246 changed pairs, all three made after P2-086:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `InterpolatedString` | 74 / 74, 21, 4 | 87 / 81, 87, 23 | 31 / 31, 4, 0 | 112 | 27 (1.2%) | 59 (2.6%) |

47 of the 59 are on `gitextensions-9860`, where every body that holds the reason is a changed pair
(87 of 87): net5.0 binds an interpolated string to `string.Format` and net6.0 to
`DefaultInterpolatedStringHandler`, so the pair is never congruent, and the reason alone is 3.2% of
that pair's changed pairs. The later censuses in the same report give 54 of 1,743 (3.1%). Count the
forms, and lower the largest one P2-102 does not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `InterpolatedString`, `Interpolation`,
`InterpolatedStringText`; ADR 0034 (per-ticket unlock rule);
`docs/tickets/done/P2-086-interpolated-string-owner.md` (its split and what it proved the two
bindings share); `docs/tickets/P2-102-integer-hole-before-a-hole-that-runs-code.md`;
`docs/tickets/P2-094-precision-string-format-against-interpolated-string.md`;
`docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `InterpolatedString` opaque nodes of `gitextensions-9860`'s changed
   pairs (a `--lower-only` run) by the first thing that keeps the string out of P2-086's rule: a
   hole with a format clause, a hole with an alignment clause, a hole of another type by kind
   (`bool`, `char`, an enum, floating point or `decimal`, `DateTime` or another struct, `object` or
   another reference type), an integer hole before a hole that runs code (P2-102), a string
   converted to `FormattableString` or `IFormattable`, a custom handler, other. For each form give
   the nodes, the changed pairs it is in, the changed pairs it alone keeps opaque, and the ticket
   that owns it or "none". Counts in `## Notes` (hole type kinds and clause kinds only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR as what both
   bindings compute, by P2-086's rule: the lowering is used only where `string.Format` and
   `DefaultInterpolatedStringHandler` give the same string and make the same calls in the same
   order, and `## Notes` says why they do for this form, including the current culture. Decide the
   representation with `equiv-decide` and log it.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator for both bindings, and the
   `IOPERATION-COVERAGE.md` rows updated.
4. On a re-run, the changed pairs of `gitextensions-9860` that hold `InterpolatedString` fall by at
   least 2% of changed pairs, or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that P2-102's form holds more than two thirds of the changed pairs alone, or
that the largest form is one where the two bindings do not compute the same string, lower nothing:
record the split and stop. More than two forms lowered means the ticket was misread.

## Out of scope
P2-102's form and its culture assumption. `string.Format` written out against the interpolated
string it becomes (P2-094). The IL fallback.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): fourth by marginal unlock.
