# P2-138 The types behind an opaque `default` are counted, and the largest one without a ticket lowers
Status: in-progress
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
`docs/tickets/done/P2-095-nullable-value-conversion-and-default.md`;
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
- **Criterion 1: the split (2026-10-10).** A census (`--lower-only`) of `gitextensions-9860` (legacy
  `bcd0c2617bdd`, modern `37797ea4dd74`) at `8e862692`, `main` when this ticket started, exit 0, 134 s, with
  a throwaway build that named each `DefaultValue` opaque by its type and origin and wrote each changed
  pair's opaque nodes. That build was never committed, and its output stays under `.corpus/`. The pair
  has 605 changed pairs of 14,073 matched (722 in P1-028's run: tickets since have made pairs congruent),
  260 of them without opaque. 54 hold `DefaultValue` (8.9%), 26 hold nothing else (4.3%). Nodes are
  counted in changed pairs only, legacy / modern. "Alone" is a changed pair whose every opaque node is a
  `DefaultValue` of that one form.

  | Type | Origin | Nodes | Changed pairs it is in | Changed pairs alone | Owner |
  |---|---|---|---|---|---|
  | User-defined struct | optional parameter | 48 / 48 | 44 | 15 | none |
  | Base class library struct: `CancellationToken` | optional parameter | 22 / 22 | 20 | 2 | none |
  | `Nullable<T>`, `T` a user-defined enum | optional parameter | 1 / 1 | 1 | 1 | none (P2-095, done, left an enum `T` out) |
  | `Nullable<T>`, `T` `System.DateTime` | the CFG's value for a null receiver of `?[]` | 1 / 1 | 1 | 0 | none (P2-095 left a struct `T` out) |
  | Unconstrained type parameter | written as `default` | 1 / 1 | 1 | 0 | none (Out of scope) |
  | Any other base class library struct, an enum, a `struct`-constrained type parameter, other | any | 0 | 0 | 0 | none |
  | Total | | 73 / 73 | 54 | 26 | |

  The two optional-parameter struct rows overlap: 13 changed pairs hold both, and 8 of the 26 pairs alone
  hold exactly those two forms and no other opaque node. So the default the compiler passes for an
  optional parameter of a struct type is in 54 - 3 = 51 changed pairs, and is all that keeps 15 + 2 + 8 =
  25 of them opaque (4.1% of 605). No `default` a person wrote of a struct type is in a changed pair, and
  no `default(T?)` of a `bool` or integral `T` is left (P2-095 landed). Over all 14,073 matched pairs the
  same forms are, in bodies per side: a user-defined struct's optional parameter 174, `CancellationToken`'s
  91, a `Nullable<T>` of a user-defined enum as an optional parameter 31, every other `Nullable<T>` form 17
  between them, a written `default` of a struct 2, of an unconstrained type parameter 1.
- **The Size guard does not trip.** `Nullable<T>` holds 1 of the 26 changed pairs alone.
- **Criterion 2: the two forms lowered** are the first two rows, which are one construct, the `default` the
  compiler passes for an optional parameter of a struct type, over the two kinds of struct the split
  names. The user-defined struct's is the largest alone with no owner; `CancellationToken`'s is the same
  code path, and without it 8 of the pairs the first form is in stay opaque.
- Decision: representation of the `default` the compiler passes for an optional struct parameter -> the constant element 0 of the type's sort, the value P1-029 already gives the `CancellationToken` an `await foreach` passes. Alternatives: an `In` input `default.<Sort>` read like `typeof`; a pure function `default.<Sort>` of no arguments. Rule: 4 (no new sort, node or input; the backend already takes an `IrConst` of an uninterpreted sort, so no ADR by `equiv-adr`'s bar test). A struct sort has no null and no other constant, so element 0 names nothing else, and two defaults of one type are one element on both sides.
- Decision: which defaults take that value -> only the compiler-passed argument, of a struct type that is no `Nullable<T>`, no type parameter, no special type and no `IrTuple` sort. Alternatives: every `default` of a closed struct type, written ones too. Rule: 4. The compiler-passed one is only ever a call's argument, so the IR never reads a field of it and "the default of each field" holds with nothing to assert; a written `default(S)` can be stored and read back, and then `field.S.F` at element 0 would have to be the field's default, which needs a constraint on an `In` input that the IR cannot state. Criterion 1 finds no written struct `default` in a changed pair, so that machinery would unlock nothing here; it stays opaque, as P2-003 left it.
- **Criterion 4: the re-run (2026-10-10).** The same census with this ticket's build, exit 0: 605 changed
  pairs, the same 13,468 congruent. Changed pairs that hold `DefaultValue` fall from 54 to 3, by 51 (8.4%
  of 605, against the 2% asked for); `changedReasonSets["DefaultValue"]` from 26 to 1; changed pairs
  without opaque rise from 260 to 285, so the lowerable share goes from 43.0% to 47.1%. Bodies holding the
  reason fall to 54 per side over all matched pairs. The 3 left are the last three rows of the split.
- **Criterion 5: no ticket filed.** 5% of 605 changed pairs is 31. What is left of the reason is in 3
  changed pairs and alone in 1 (`Nullable<T>` of an enum as an optional parameter), so `docs/ROADMAP.md`
  is unchanged.
- A `default` literal a person writes for a struct is not this reason at all: Roslyn makes it a conversion of
  the literal, so it is an opaque `Conversion` (P2-123's "other" form), and only `default(S)` is `DefaultValue`.
- Roslyn gives an optional parameter whose default is a compile-time constant a literal, not an
  `IDefaultValueOperation`, so the new arm never sees an `int`, an enum, a `decimal` or an `nint`
  (`AConstantDefaultIsStillItsConstant`).
- The oracle's form is `o.Nudge(k)`, drawn only by `LoweringOracleGen.MethodWithLambda`, as P2-095's and
  P2-136's are, so `Method`'s draws and its other consumers are as they were. `Nudge` adds `k + m.N`, so the
  compiled run and the IR agree only if the value passed is the default mark.
- Precision limit: element 0 is the default only by being the one value every omitted argument of the type
  is. Nothing says `new S()` or a field's initial value is that element, so a pair whose one side omits the
  argument and whose other side writes `new S()` or `default(S)` is still not proved.
- Sample snapshots were not rerun locally (the gate runs in CI): a sample whose body leaves out an optional
  struct parameter loses its `DefaultValue` opaque.
