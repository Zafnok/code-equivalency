# P2-170 A deconstruction of a tuple-typed value lowers as its stores, not as an opaque
Status: done (PR #488)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-025

## Goal
P2-025 lowers `(a, b) = (x, y)`, a deconstruction of a tuple literal. `IOPERATION-COVERAGE.md`'s
`DeconstructionAssignment` row lists what it left opaque: a tuple-typed value, a `Deconstruct`
method, a nested tuple, a property or array element target, and a deconstruction whose value is
used.

On `gitextensions-11284` (IDE0008, explicit types for `var`) that remainder is the largest single
reason a changed pair is not decided. In P2-110's quick run at equiv `78853202`
(`docs/runs/2026-10-13-cleanup-gitextensions-11284/SUMMARY.md`):
- `DeconstructionAssignment` is in the reason set of 46 of the 178 changed pairs (25.8%), and the
  whole reason set of 28;
- it is the first reason of 34 Unknowns, the largest group of the review list;
- the pull request rewrites 38 deconstructions from `var (a, b) = ...` to explicit types. By a text
  scan of the diff, 26 of the 38 take apart the result of a call, 9 an array element, 2 an awaited
  value and 1 a new object. None takes apart a tuple literal. So the form to lower first is a tuple-typed value.

In the thorough run of the same pair the IL pass proves 22 pairs that are `opaque` in the quick
run, and the review-list group falls from 34 to 18. So a good part of these pairs is provable once
the construct lowers. Quick mode, the default, proves none of them.

When done, a deconstruction of a tuple-typed value into locals lowers as one evaluation of the
value and one store per element, in the order the language gives, and the two spellings (`var (a,
b)` and `(T a, U b)`) lower to the same IR.

## Spec references
`IOPERATION-COVERAGE.md` rows `DeconstructionAssignment` and `Tuple`; VERIFICATION-MODEL section 3;
`docs/tickets/done/P2-025-deconstruction-assignment.md` and P2-027 (the `IrTuple` sort and its
limits); `.claude/skills/equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over both sides of `gitextensions-11284`, how many opaque deconstructions each form
   in the coverage row accounts for (tuple-typed value by where the value comes from and by element
   types; `Deconstruct` method; nested; non-local target; value used). Record the table in Notes.
2. The largest form lowers, and `IOPERATION-COVERAGE.md`'s row says so. If the largest form needs a
   tuple sort P2-027 does not have (an element that is not `bool` or integral), say in Notes what
   was done about it through `equiv-decide`.
3. A sample pair that changes only `var (a, b) = F()` to `(T a, U b) = F()` is Equivalent; a sample
   that swaps the two targets is Divergent.
4. Whatever stays opaque still writes its local and parameter targets with unknown values (P2-009).
5. Rerun `gitextensions-11284` in compare mode quick. Notes record the reason-set count next to 46
   and the solver-proved count next to 73 of 178.

## Tests
- `IrLowererTests` and `IrLowererSnapshotTests`: one per new shape, and one for a shape that stays
  opaque.
- `DeconstructionEquivalenceTests`: criterion 3's two samples.
- `LoweringOracleTests` covers the new shape.

## Size guard
More than one form of the coverage row lowered: stop, the next form is its own ticket.

## Out of scope
A `Deconstruct` method unless criterion 1 finds it the largest form; the IL pass.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No open ticket owned the reason.
- Criterion 1, the count. Every opaque deconstruction of `gitextensions-11284`, by a lower-only run at
  equiv `0a2f9a03` with a temporary log line in `IrLowerer.Opaque` (not committed). The two sides
  are alike, so each number is one side's. 102 sites, in 79 bodies (the census's 79):

  | form of the coverage row | sites |
  |---|---|
  | tuple-typed value | 80 |
  | `Deconstruct` method | 22 |
  | nested tuple target | 1 (one of the 22) |
  | property or array element target | 2 (two of the 80, both a property) |
  | value used | 0 |
  | tuple literal the lowering does not take | 0 |

  The 80 of a tuple-typed value, by where the value comes from: a call 60, a property 13 (6 of them
  the `Current` of a `foreach` that deconstructs its variable), an awaited value 3, an array element
  2, an indexer through `^` or a range 1, a local 1. By where they stand: a statement 74, a `foreach`
  variable 6. By target: locals 55, locals and discards 14, fields 8, a parameter 1, a property 2.
  All 80 reach their targets through an identity conversion.

  The 80 by element types:

  | elements | sites |
  |---|---|
  | all `bool` or integral, two or three of them (P2-027's sort) | 4 |
  | every element a reference | 45 (35 pairs, 10 triples) |
  | references and `bool` or integral elements | 17 |
  | at least one struct that is neither | 13 |
  | an enum and an integral element | 1 |

  The 22 through a `Deconstruct` method: 21 are the variable of a `foreach`, 1 a statement.

  The ticket's text scan of the diff (26 calls, 9 array elements, 2 awaited, 1 new object, of 38) counts
  the deconstructions the pull request rewrote; this counts every opaque one of both solutions.
- Criterion 2. The largest form is the tuple-typed value, and 76 of its 80 sites have an element
  P2-027's sort does not hold.
- Decision: an element of a tuple outside P2-027's sort -> a read of the element's
  `field.System.ValueTuple_<arity>.Item<n>` map at the tuple, the map `t.Item<n>` already reads
  (`Tuple`'s row). Alternatives: widen `IrTuple` to any element type (a Core and Z3 change, and a
  second meaning for a tuple that `t.Item1` reads as a map); a new IR instruction. Rule: 4.
- Decision: the element's map is the one of its position (`Item<n>`), whatever the tuple's type names
  the element -> a deconstruction has no name to read by. So `var (a, b) = t` is `a = t.Item1; b =
  t.Item2`, and is not `a = t.Name` where the type names the element, which is another map (P2-027's
  Notes). Alternatives: the named field's map. Rule: 3.
- Decision: the null shadow of a reference target -> the `null.<Sort>` map read at the element, as
  for any value with no shadow of its own. Alternatives: none that the IR has. Rule: 4.
- Decision: only a value that reaches the targets through an identity conversion is taken apart; one
  that converts an element (`(long a, int b) = intPair`) stays opaque -> no site of the census
  converts. Alternatives: lower each element's conversion. Rule: 4.
- Decision: the deconstruction a `foreach` makes of its variable is the same form -> it is lowered
  where a statement of the graph is, since the graph does not wrap it in an expression statement and
  nothing uses its value. 6 of the 80 sites. Alternatives: leave it opaque. Rule: 4.
- Decision: a tuple of more than seven elements, one that holds a struct whose read is opaque with
  reason `Layout` (P2-161), and one whose `Item<n>` map the body already holds at another type stay
  opaque -> `HeapInputs.Holds` asks without adding the map. Alternatives: lower and let the read be
  ill-typed. Rule: 4.
- Found on the way, filed as P2-174: a field's map is named without its type's arguments, so a
  `(string, int)` and an `(int, string)` in one body share `field.System.ValueTuple_2.Item1`, at the
  type of whichever came first. `int m = q.Item1; string s = p.Item1;` makes the lowerer throw today,
  without any deconstruction. The last decision above is what keeps this ticket's reads well typed.
- Criterion 3. The Divergent sample's tuple holds a string, so its elements are maps. P2-027's Notes
  say a divergence that flows through `tuple.item` can be reported Unknown and not Divergent; a swap
  of an `(int, int)` was not tried here.
- Criterion 5. `gitextensions-11284`, compare mode quick, this branch on `0a2f9a03`, no `--jobs` (24
  processors), 280 s, exit 1, no `run.properties.unverified`, 0 notifications. Against P2-110's
  quick run at `78853202` (`docs/runs/2026-10-13-cleanup-gitextensions-11284/SUMMARY.md`):

  | | P2-110 | this run |
  |---|---|---|
  | changed pairs with `DeconstructionAssignment` in the reason set | 46 of 178 (25.8%) | 16 of 178 (9.0%) |
  | changed pairs whose whole reason set it is | 28 | 5 |
  | changed pairs without opaque | 56 (31.5%) | 79 (44.4%) |
  | proved Equivalent by the solver | 73 (41.0%): lockstep-induction 52, bounded 21 | 99 (55.6%): lockstep-induction 56, bounded 43 |
  | Divergent | 1 | 1, the same `BugReporter.Program::Main()` |
  | Unknown over matched pairs | 104: opaque 82, timeout 17, abstraction 5 | 78: opaque 55, timeout 18, abstraction 5 |
  | `EQ003 opaque:DeconstructionAssignment` in the review list | 34 | 9 |
  | bodies with a `DeconstructionAssignment` opaque, a side | 79 | 23 |

  Result by result against P2-110's SARIF: 26 Unknowns became Equivalent, 2 `opaque` Unknowns became
  `timeout`, and no Equivalent and no Divergent changed. No other reason's count in the changed
  reason sets moved. One more body a side has a `LocalFunction` opaque (218 to 219); which one was
  not looked up.
- The 24 sites a side that stay opaque (22 `Deconstruct` method, 2 property target) are P2-175.
- No `docs/runs/` file was added (the criterion asks for Notes), so the README's scoreboard is as it was.
