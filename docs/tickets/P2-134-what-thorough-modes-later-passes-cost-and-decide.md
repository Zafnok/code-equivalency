# P2-134 Thorough mode's later passes cost ten hours on Git Extensions and prove nothing: set their budgets from the measurement
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-032

## Goal
P1-032 made thorough the default and measured it on `gitextensions-8522` with `--jobs 4` (its Notes hold the
tables). Quick takes 999 s. Thorough takes 37,184 s, and its later passes are where the time goes:

- the budget pass (`bound` 8, `resourceLimit` 30,000,000, `timeoutMs` 600,000) verifies 255 pairs again in 30,222 s
  and decides 29, all Divergent (27 EQ006, 2 EQ002), and no Equivalent; 67 more leave `timeout` for another reason;
- the IL pass verifies 38 pairs in 5,794 s at the budget pass's values and decides 13 (1 Equivalent, 5 EQ002, 7
  EQ006). A run at the first pass's values with `resourceLimit` 5,000,000 verified 40 pairs in 327 s and decided the
  same numbers of each. P1-032 chose the larger values for this pass; ADR 0049 names none.

ADR 0049 estimated the budget pass at about 28,500 s on one thread from a measurement at `bound` 3. The bound of 8
was not measured before P1-032, and neither were ADR 0037's queries at 30,000,000. When done, each value of the
budget pass and the IL pass is one a measurement chose, and thorough's cost on this pair is known per knob.

## Spec references
ADR 0049 (the mode table, decisions 4 and 7), ADR 0037, ticket P1-032's Notes, `docs/runs/2026-10-01-timeout-budget.md`,
`src/Equiv.Cli/Passes.cs`, `src/Equiv.Cli/CompareCommand.cs` (`LaterPasses`), `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. On `gitextensions-8522`, thorough compare mode, `--jobs 4`, one run for each of: the escalation as it is; `bound`
   3 with the other two as they are; `resourceLimit` 5,000,000 with the other two as they are; and the escalation as
   it is with the IL pass at the first pass's values. `docs/runs/<date>-thorough-budgets.md` gives, for each: the
   time of each later pass, the results each produced by rule and by Unknown reason, and `queryEndings`.
2. From that table, the IL pass's values are chosen by `equiv-decide` and logged as a `Decision:` line. ADR 0049
   names none, so this needs no ADR.
3. If the table says a value of ADR 0049's mode table should change, that goes through `equiv-adr` as a
   clarification of ADR 0049 with the table as its evidence, before any code. If it says none should, Notes says so
   and why.
4. VERIFICATION-MODEL.md section 6 and the README state what thorough costs against quick on this pair.

## Files
`src/Equiv.Cli/Passes.cs`, `src/Equiv.Cli/CompareCommand.cs`, their tests, `docs/runs/`, `docs/VERIFICATION-MODEL.md`,
`README.md`, and ADR 0049 only through `equiv-adr`.

## Tests
`Thorough_IlPass_UsesTheChosenBudgets`, and the mode table's row tests for any value that changes.

## Size guard
Four runs of about ten hours each. If a run cannot finish in a day, record how far it got and stop.

## Out of scope
Parallel verification past four threads (P2-132). Whether thorough is the default (ADR 0049 decides that).
The IL pass's false Divergents (P2-135).

## Notes
- Filed by P1-032 on 2026-10-06 from criterion 11's runs.
