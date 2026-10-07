# P2-138 The types behind an opaque `default` are counted, and the largest one without a ticket lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A `default` that is not a compile-time constant is an opaque node with reason `DefaultValue`: a
struct type's, and an unconstrained or `struct`-constrained type parameter's
(`IOPERATION-COVERAGE.md`). P2-095 owns one form, `default(T?)`. Nobody has counted the others.
P1-028's row (`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246 changed
pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `DefaultValue` | 315 / 315, 45, 14 | 355 / 355, 66, 11 | 261 / 261, 37, 1 | 148 | 26 (1.2%) | 73 (3.3%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns; `Conversion+DefaultValue` is 22 of them, the shape P2-095 describes. The later censuses
in the same report give 58 of 1,743 (3.3%). Count the forms, and lower the largest one P2-095 does
not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `DefaultValue`; ADR 0034 (per-ticket unlock rule);
`docs/tickets/P2-095-nullable-value-conversion-and-default.md`;
`docs/tickets/done/P2-003-default-value.md`; `docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `DefaultValue` opaque nodes of `gitextensions-9860`'s changed pairs (a
   `--lower-only` run) by the type whose default it is: `Nullable<T>` (P2-095), a base class library
   struct by name (`CancellationToken`, `DateTime`, `Guid`, `TimeSpan`, a `ValueTuple`, ...), a
   user-defined struct, an enum, an unconstrained type parameter, a `struct`-constrained type
   parameter, other; and by where it comes from: written as `default`, the value the compiler
   passes for an optional parameter, the value the CFG gives `?.` for a null receiver. For each form
   give the nodes, the changed pairs it is in, the changed pairs it alone keeps opaque, and the
   ticket that owns it or "none". Counts in `## Notes` (type kinds and base class library type names
   only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR as one value per
   type that both sides share, so that two defaults of one type are equal and a default of a struct
   whose fields the IR reads has the default of each field. Decide the representation with
   `equiv-decide` and log it; if it needs a sort or an IR node the IR does not have, go through
   `equiv-adr`'s bar test first.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, the changed pairs of `gitextensions-9860` that hold `DefaultValue` fall by at least
   2% of changed pairs, or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that `Nullable<T>` holds more than two thirds of the changed pairs alone, lower
nothing: record the split and stop. More than two forms lowered means the ticket was misread.

## Out of scope
`default(T?)` and the conversion to `Nullable<T>` (P2-095). A type parameter's default where the
body is compared over every instantiation at once, unless criterion 1 makes it the largest form.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): third by marginal unlock.
