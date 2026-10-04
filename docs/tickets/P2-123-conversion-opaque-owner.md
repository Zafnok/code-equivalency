# P2-123 The conversion forms behind `Conversion` are counted, and the largest one without a ticket lowers
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
A conversion the frontend does not lower is an opaque node with reason `Conversion`: "anything else"
in the `Conversion` row of `IOPERATION-COVERAGE.md`, which names a nullable conversion and an
unboxing among them. P2-099 took collection expressions and target-typed `new()` out of the reason.
What is left, alone, keeps this share of changed pairs opaque:
- `gitextensions-9860` (pull request 371's run): 110 of 722 (15.2%), and it is in at least 69 more
  with one other reason;
- `jellyfin-13023` (the same pull request): 29 of 573 (5.1%);
- `gitextensions-11372`: 22 of 351 (6.3%), in 650 bodies per side, P2-099's rerun;
- `gitextensions-8522`: 22 of 1,143 (1.9%), in 100, P2-046's run.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. The run reports name
P1-014, the IL fallback, which P1-018 left off by default, or "none". P2-095 owns one form, a
conversion to `Nullable<T>`. Count the forms, and lower the largest one P2-095 does not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `Conversion`; ADR 0034 (per-ticket unlock rule); M4-002
(`conv.*` functions), M4-005 (casts as type tests), M3-010 (the `cast` maps).

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `Conversion` opaque nodes of `gitextensions-9860`'s changed pairs (a
   `--lower-only` run) by form: to `Nullable<T>`, from `Nullable<T>`, unboxing, boxing of a type
   parameter, enum to or from its underlying type, a user-defined conversion whose operand or result
   is not its method's own type, pointer or `nint`, `dynamic`, other. For each form give the nodes,
   the changed pairs it is in, the changed pairs it alone keeps opaque, and the ticket that owns it
   or "none". Counts in `## Notes` (conversion kinds and types only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR. Decide the
   representation with `equiv-decide` and log it; if it needs an IR node or a sort the IR does not
   have, go through `equiv-adr`'s bar test first.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, `changedReasonSets["Conversion"]` on `gitextensions-9860` falls by at least 5% of
   changed pairs (36 pairs), or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that the form P2-095 owns holds more than two thirds of the changed pairs
alone, lower nothing: record the split and stop. More than two forms lowered means the ticket was
misread.

## Out of scope
The IL fallback. A conversion to `Nullable<T>` (P2-095). Collection expressions (P2-120).

## Notes
- Found by the 2026-10-03 review of the open tickets against the Unknown reasons of the three large
  runs. `Conversion` alone is the largest reason set on `gitextensions-9860` after the pairs with no
  opaque, and it has no open owner.
- `gitextensions-9860` is in `tools/corpus/pairs.csv` once P2-066 is done, hence the dependency.
- **The Goal's figure for `gitextensions-9860` predates P2-099.** Pull request 371's run was made at
  `8e0ed3c`, and P2-099 merged after it (`c1d1d5f`), so the 110 of 722 still held every target-typed
  `new()`. Three censuses (`--lower-only`) of the same checkouts (legacy `bcd0c2617bdd`, modern
  `37797ea4dd74`), each exit 5 on the three P2-105 crashes, about two minutes each (2026-10-03):

  | equiv | changed pairs | without opaque | `changedReasonSets["Conversion"]` | changed pairs holding the reason | bodies holding it (legacy / modern) |
  |---|---|---|---|---|---|
  | `92904e1`, the commit before P2-099 | 722 | 218 | 110 (15.2%) | 259 | 2,019 / 2,019 |
  | `c1d1d5f`, P2-099 | 722 | 312 | 16 (2.2%) | 44 | 564 / 564 |
  | `2c2f682`, `main` when this ticket started | 725 | 314 | 16 (2.2%) | 44 | 564 / 564 |

  So P2-099 alone took the reason from 15.2% to 2.2% on this pair, and the lowerable share from 30.2%
  to 43.2%. `jellyfin-13023`'s 29 of 573 is from the same `8e0ed3c` run and was not measured again
  here; P2-124 reruns both upgrade pairs on one commit.
- **Criterion 1: the split (2026-10-03).** The `2c2f682` census, with a throwaway build that named
  each `Conversion` opaque by its form, conversion kind and operand and result type kinds, and wrote
  each changed pair's opaque nodes. That build was never committed, and its output stays under
  `.corpus/`. Nodes are counted in changed pairs only, legacy / modern. "Alone" is a changed pair
  whose every opaque node is a `Conversion` of that one form. No changed pair holds two forms.

  | Form | Nodes | Changed pairs it is in | Changed pairs alone | Owner |
  |---|---|---|---|---|
  | To `Nullable<T>` | 59 / 59 | 34 | 13 | P2-095 |
  | From `Nullable<T>` | 0 | 0 | 0 | none |
  | Unboxing | 0 | 0 | 0 | none |
  | Boxing of a type parameter | 0 | 0 | 0 | none |
  | Enum to or from its underlying type | 3 / 3 | 1 | 1 | none |
  | User-defined, operand or result not the method's own type | 0 | 0 | 0 | none |
  | Pointer or `nint` | 7 / 7 | 4 | 1 | none |
  | `dynamic` | 0 | 0 | 0 | none |
  | Other | 6 / 6 | 5 | 1 | none |
  | Total | 75 / 75 | 44 | 16 | |

  To `Nullable<T>`, by `T` (all implicit): `int` 28 nodes, `bool` 14, `char` 6, `long` 3, `uint` 1,
  `System.DateTime` 3, another struct 3, an enum 1. Of the 13 pairs alone, 11 have an `int` or `bool`
  `T`, which is P2-095's scope; 1 has a struct `T` and 1 an enum `T`, which P2-095 leaves out. The
  operand is a literal in 28 of the 59 nodes. Enum: `(int)` of an enum-typed property, explicit.
  Pointer or `nint`: `IntPtr` to a pointer and back (2 nodes), a pointer to `ulong` (1), the `null`
  literal to a pointer (4). Other: a `default` literal to a struct (3 nodes, 3 pairs), a tuple literal
  to a tuple type (2 nodes, 1 pair), and a lambda converted to a class type that is not a delegate
  type (1 node, the 1 pair alone). Over all 14,020 matched pairs the unboxing form is in at most 53
  bodies per side and `dynamic` in 3, none of them in a changed pair.
- **The Size guard trips: nothing is lowered (criteria 2 and 3).** The form P2-095 owns holds 13 of
  the 16 changed pairs alone (81%), and 11 of 16 (69%) counting only the `T` P2-095 covers. Both are
  over two thirds. No code, no test and no `IOPERATION-COVERAGE.md` change: the `Conversion` row is
  as P2-099 left it.
- **Criterion 4: why the reason does not fall by 36 pairs.** It no longer holds 36. P2-099 took 94
  of the 110, before this ticket, and 16 are left.
- **Criterion 5: no ticket filed.** 5% of 725 changed pairs is 37. Every form without an owner holds
  1 pair alone, and `Conversion` as a whole, at 2.2%, is under ADR 0028's line on this pair. P2-095
  is the open owner of what is left: 13 of the 16.
