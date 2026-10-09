# P2-122 The pattern forms behind `switch-pattern` are counted, and the largest one without a ticket lowers
Status: done (PR #444)
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
- **Criterion 1: the split (2026-10-09).** A census (`--lower-only`, exit 0, about two minutes) of
  `gitextensions-8522` (legacy `3f4ed21998af`, modern `5190ba5c1a5f`) at `c8bf2024`, `main` when this
  ticket started, with a throwaway build that named each `switch-pattern` opaque by its pattern
  kinds and the type kinds of its constant and scrutinee, and wrote each changed pair's opaque
  nodes. That build was never committed, and its output stays under `.corpus/`. The pair now has
  910 changed pairs of 13,592 matched, 377 of them without opaque; the Goal's 1,143 and 79 are
  P2-046's run. `switch-pattern` is in 151 changed pairs and alone in 67 (7.4%). Nodes are counted in
  changed pairs only, legacy / modern. A form is the outermost pattern. "Alone" is a changed pair
  whose every opaque node is a `switch-pattern` of that one form.

  | Form | Nodes | Changed pairs it is in | Changed pairs alone | Owner |
  |---|---|---|---|---|
  | Constant: `null` on a reference type | 117 / 115 | 93 | 37 | none |
  | Constant: `null` on a `Nullable<T>` | 4 / 4 | 3 | 0 | none |
  | Constant: a `string` | 2 / 2 | 1 | 0 | none |
  | Constant: an enum | 0 | 0 | 0 | none |
  | Relational | 0 | 0 | 0 | P2-093 |
  | Negated: `not null` on a reference type | 121 / 119 | 78 | 19 | P2-104 |
  | Negated: `not null` on a `Nullable<T>` | 4 / 4 | 3 | 0 | P2-104 |
  | Negated: a type or declaration pattern | 3 / 3 | 2 | 0 | P2-104 |
  | Negated: `or` of two enum constants | 1 / 1 | 1 | 0 | P2-104 |
  | `and` / `or` | 2 / 2 | 2 | 1 | none |
  | `var` | 0 | 0 | 0 | none |
  | Property or positional | 9 / 9 | 2 | 0 | none |
  | List | 0 | 0 | 0 | none |
  | An unboxing or generic type test | 0 | 0 | 0 | none |
  | Other | 0 | 0 | 0 | none |
  | Total | 263 / 259 | 151 | 57, and 10 more with two forms | |

  The 10 pairs alone under two forms: `null` with `not null`, both on a reference, 8; `null` on a
  reference with a negated type pattern, 1; a `string` constant with `not null`, 1. The reference
  type under `null` and `not null` is `string` in 73 / 72 of the 238 / 234 nodes. Relational patterns
  appear only inside an `and` (2 pairs); property or positional is one `{ P: ... } x` after a type
  and one pair of 8 positional patterns on a struct. P2-103's form (`as` plus a null check against
  `is T t`) is two lowered constructs and never a `switch-pattern` node, so it has no row.
- **The Size guard does not trip.** The forms P2-104 owns hold 19 of the 67 pairs alone (28%), and
  P2-093's and P2-103's hold none.
- **Criterion 2: `x is null` on a reference type lowers.** It is the largest form with no owner: 37
  pairs alone. No IR node and no sort is new, so `equiv-adr` was not needed.
- Decision: representation of a `null` constant pattern on a reference-typed scrutinee -> the null flag `x == null` lowers to (`NullFlag`: the variable's shadow, else a read of `null.<Sort>`), with no call and no exception edge. Alternatives: an `Eq` against a null constant of the sort; a new IR test. Rule: 4.
- Decision: `null` on a `Nullable<T>` or on a type parameter not known to be a reference -> stays opaque with reason `switch-pattern`. Alternatives: lower `Nullable<T>` through its null shadow as `IsNull` does. Rule: 4. `x == null` on a nullable value is opaque too (P2-083), and the form holds no pair alone.
- **Criterion 4: the reason falls by 37 pairs, 4.1% of the 910 changed pairs, under the 5% asked
  for.** A second census at this branch's lowering (same checkouts, exit 0):
  `changedReasonSets["switch-pattern"]` 67 -> 30, changed pairs holding the reason 151 -> 84, changed
  pairs without opaque 377 -> 414 (41.4% -> 45.5% lowerable). 5% would be 46 pairs, and the form
  holds only 37 alone; every other form without an owner holds at most 1. Of the 30 left, 27 are
  `not null` on a reference alone (the 19, and the 8 that also held `null`), 1 is a negated type
  pattern, 1 is an `and`, and 1 is a `string` constant with `not null`. P2-104 owns 28 of them, and
  its inner `null` pattern now lowers.
- **Criterion 5: no ticket filed.** 5% of 910 is 46. No form without an owner holds more than 1 pair
  alone after this change. `gitextensions-11372`, `gitextensions-9860` and `jellyfin-13023` were not
  split here.
