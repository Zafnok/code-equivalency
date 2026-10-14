# P2-181 An unedited body that a runtime rule keeps out of congruence is proved where the rule's change cannot be reached
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
On a pair that crosses a runtime, a body whose two texts are equal is still not congruent when it
calls a runtime-sensitive member (ADR 0040; `properties.notCongruent` component `runtime-rule`, ticket
P2-173). The solver then has to decide it. P2-130's three migration runs (equiv `e98fd1b9`, compare
mode quick, `docs/runs/2026-10-14-migrations-verdict.md`) show it almost never does:

| Pair | Changed pairs | `runtime-rule` | proved | EQ006 | Unknown (abstraction, timeout, opaque, other) |
|---|---|---|---|---|---|
| gitextensions-8522 | 938 | 493 (52.6%) | 4 | 150 | 339 (201, 76, 54, 8) |
| duplicati-3124 | 707 | 269 (38.0%) | 1 | 122 | 146 (66, 62, 13, 5) |
| openra-17989 | 321 | 242 (75.4%) | 0 | 47 | 195 (81, 85, 22, 7) |
| All three | 1,966 | 1,004 (51.1%) | 5 | 319 | 680 (348, 223, 89, 20) |

So half the migrations' changed pairs are code nobody edited, and they are 680 of the 1,222 Unknown
results (55.6%) and 319 of the 411 Divergent (77.6%). It is the largest cause of both of the
scoreboard's weak rows, and no ticket owns it: P2-073 to P2-075 narrowed when a row fires EQ006, not
what happens to a body after it is taken out of congruence, and P2-173 left pairs that cross a runtime
out.

When done, an unedited body whose runtime-sensitive calls cannot reach their rows' documented change
(the same preconditions P2-073 and P2-074 use for EQ006) is Equivalent, and one whose calls can is
Unknown or EQ006 as today.

## Spec references
ADR 0040, ADR 0008, VERIFICATION-MODEL section 3 (runtime-changed members) and the `notCongruent`
table, `src/Equiv.Core/RuntimeChanges/runtime-changes.json` and its preconditions,
`docs/tickets/done/P2-073-*.md`, `P2-074-*.md`, `P2-113-*.md`, `P2-173-*.md`; `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, over the three migration pairs at `main`'s head, the 1,004 `runtime-rule` pairs by the row
   or rows their body reaches, and for each Unknown the runtime-sensitive function its candidate
   depends on. Notes hold the top rows and functions with counts.
2. Decide, through `equiv-adr`, when a body with equal texts whose runtime-sensitive calls cannot reach
   a row's change may be Equivalent (for example: congruent when every such call's row precondition is
   false by its arguments, or the two sides' functions shared where the precondition is false), and
   why that cannot hide a reachable change.
3. Implement it, with unit tests and a sample pair whose unedited body calls a path row with a
   constant path and another with a parameter.
4. Rerun the three migration pairs in compare mode quick and record, in Notes, the `runtime-rule`
   pairs by outcome next to the table above, and that every result that was EQ006 is still EQ006 or
   has a documented precondition that its arguments make false.

## Tests
As criterion 3; a soundness test in which the change is reachable and the pair is not Equivalent.

## Size guard
New runtime rows: stop, that is catalogue work.

## Out of scope
`extern` methods (P2-165). Pairs on one runtime.

## Notes
- Filed 2026-10-14 by P2-130 from its three migration runs: the largest unowned cause of Unknown and
  of Divergent results on the scoreboard's large pairs.
