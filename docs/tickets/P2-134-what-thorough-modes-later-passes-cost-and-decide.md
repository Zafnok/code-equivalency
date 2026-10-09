# P2-134 Thorough mode's later passes cost ten hours on Git Extensions and prove nothing: set their budgets from the measurement
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-032

## Goal
P1-032 built thorough mode and measured it on `gitextensions-8522` with `--jobs 4` (its Notes hold the
tables), and ADR 0052 made quick the default because of what it cost. Quick takes 999 s. Thorough takes 37,184 s, and its later passes are where the time goes:

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
Parallel verification past four threads (P2-132). Whether thorough is the default (ADR 0052 decides that; this ticket's table is what a later ADR would revisit it with).
The IL pass's false Divergents (P2-135).

## Notes
- Filed by P1-032 on 2026-10-06 from criterion 11's runs.
- Criterion 1: `docs/runs/2026-10-08-thorough-budgets.md`. Five runs on `main` at `b68abf40`, one after another,
  `--jobs 4`: quick (368 s) for criterion 4, then A the escalation as it is (25,865 s), B `bound` 3 (10,870 s), C
  `resourceLimit` 5,000,000 (2,715 s), D the IL pass at the first pass's values (22,318 s). B and C set their value
  with `--config` and an `escalation` key. D needed a one-line local patch (`passes.Whole` for the IL pass's options
  in `CompareCommand.LaterPasses`), which is not in this PR; the report says how to make it again. No run came near
  the size guard's day.
- The ticket's numbers were P1-032's build. On `b68abf40` the budget pass has 168 pairs (was 255), the IL pass 34
  (was 38), quick takes 368 s (was 999 s) and leaves 106 `timeout` Unknowns (was 183): P1-030, P1-031 and P1-038
  landed in between. The first pass's `resourceLimit` is 2,000,000, as ADR 0049's table says.
- Criterion 3: one value of ADR 0049's table changes, the budget pass's `bound`, 8 to 3, as a dated clarification of
  ADR 0049 written before the code. At 8 the pass took 22,982 s for 13 Divergents; at 3 it took 9,113 s for 15, and
  no Divergent at 8 needed more than three iterations. `resourceLimit` 30,000,000 stays: at 5,000,000 the pass
  reports 5 Divergents, so the larger limit is where its answers come from, and ADR 0049 decision 7 says a low yield
  places work in thorough and does not remove it. `timeoutMs` 600,000 stays: it was not varied and ended at most 5
  queries of a run.
- Decision: the IL pass's values -> the budget pass's (the first pass's when there is no budget pass), which is 3 / 30,000,000 / 600,000 by default after the clarification. Alternatives: the first pass's values (3 / 2,000,000 / 60,000), a third set of its own. Rule: 4. The table: at the budget pass's values the pass produced 14 results in 1,072 s (run B); at the first pass's, 12 in 28 s (run D), the same proof and the same four EQ002 and two fewer EQ006. Each of those two costs 522 s, about what the budget pass pays for a Divergent at the same limit (608 s), so the reasoning that keeps 30,000,000 for the budget pass keeps it here; and it is no code change, since the pass already follows the budget pass.
- Decision: a pair with a loop or a self-call and no step that hit a budget stays a candidate of the budget pass at `bound` 3 -> no change to `LaterPasses`. Alternatives: skip such a pair when the escalation's bound is the first pass's. Rule: 4. The 41 such pairs cost 88 s between them in run B, and ADR 0049's table names them.
- Deviation: `src/Equiv.Core/Configuration/Escalation.cs` and its test in `EquivConfigLoaderTests` are outside the
  Files list. The default the mode table's row reads lives there, so the changed value is changed there.
- Run A shared the machine for about 75 minutes of its budget pass with other sessions' builds and short `equiv`
  runs, and run D ran under a load the run script could not name. Runs A and D have the same budget pass and took
  22,982 s and 21,638 s, so that is worth about 6%; the report says so.
- The IL pass fails on one pair in every run (`GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()`,
  P2-078, open), which keeps its result with a warning, as ADR 0049 decision 5 says.
- Scoreboard (`equiv-scoreboard`): unchanged. Its rows rest on runs in the default mode, which is quick; these are
  thorough runs and a quick run made only to compare against, on one pair.
