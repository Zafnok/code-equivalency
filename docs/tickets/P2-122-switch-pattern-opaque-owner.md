# P2-122 The pattern forms behind `switch-pattern` are counted, and the largest one without a ticket lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A pattern the frontend does not lower is an opaque node with reason `switch-pattern`: "every other
pattern" in the `IsPattern` row of `IOPERATION-COVERAGE.md`. Alone, it keeps this share of changed
pairs opaque:
- `gitextensions-8522`: 79 of 1,143 (6.9%), and it is in 203, P2-046's run;
- `gitextensions-11372`: 16 of 351 alone and 15 more with `CollectionExpression`, in 1,246 bodies
  per side, P2-099's rerun;
- `gitextensions-9860` and `jellyfin-13023` (pull request 371's runs): 49 of 722 (6.8%) and 22 of
  573 (3.8%) alone, and it is in five more of Jellyfin's fifteen largest reason sets.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. Every run report names
P1-014, the IL fallback, and P1-018 left that off by default, so the reason has no open owner. Three
tickets each own one form: P2-093 a relational pattern, P2-103 `as` plus a null check against
`is T t`, P2-104 a negated pattern. Nobody has counted the forms. Count them, and lower the largest
one those tickets do not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `IsPattern`, `DeclarationPattern`, `TypePattern`,
`ConstantPattern`; ADR 0034 (per-ticket unlock rule); M4-005 (type tests);
`docs/runs/2026-09-30-full-verdict.md` (the per-ticket unlock table).

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `switch-pattern` opaque nodes of `gitextensions-8522`'s changed pairs
   (a `--lower-only` run) by form: constant of a type other than the scrutinee's bitvector or Bool
   type (a `string`, an enum, `null`), relational, negated, `and`/`or`, `var`, property or
   positional, list, an unboxing or generic type test, other. For each form give the nodes, the
   changed pairs it is in, the changed pairs it alone keeps opaque, and the ticket that owns it or
   "none". Counts in `## Notes` (pattern kinds and types only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR, by the rules the
   lowered patterns already follow: a test is a Bool value, a binding is written whether or not the
   test passes. Decide the representation with `equiv-decide` and log it; if it needs an IR node or a
   sort the IR does not have, go through `equiv-adr`'s bar test first.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` rows updated.
4. On a re-run, `changedReasonSets["switch-pattern"]` on `gitextensions-8522` falls by at least 5%
   of changed pairs, or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that the forms P2-093, P2-103 and P2-104 own hold more than two thirds of the
changed pairs alone, lower nothing: record the split and stop. More than two forms lowered means the
ticket was misread.

## Out of scope
The IL fallback. The forms P2-093, P2-103 and P2-104 own. `SwitchChains` and `IrSwitch`.

## Notes
- Found by the 2026-10-03 review of the open tickets against the Unknown reasons of the three large
  runs: "opaque construct" is the largest reason (733 of 1,471), and `switch-pattern` is its largest
  part with no open owner.
