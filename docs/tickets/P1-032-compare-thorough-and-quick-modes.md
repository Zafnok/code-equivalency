# P1-032 `equiv compare --mode thorough|quick`: thorough is quick plus further passes over what is still Unknown
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-013, P1-016, P1-010, P2-050, P2-076

## Goal
ADR 0049. A migration run should spend machine time to leave fewer Unknowns; a diff check should
come back quickly. Today there is one setting. When done, `compare` has two modes. Both run the same
first pass. `thorough` (the default) then verifies again, with more budget and from IL, only the
pairs that are still Unknown, asks ADR 0037's queries on `timeout` Unknowns, and runs the contracts
pass. `quick` stops after the first pass. No verdict means anything different in either mode.

## Spec references
ADR 0049 (the mode table is the specification; do not change a value in it), ADR 0037, ADR 0039,
ADR 0036, ADR 0023, ADR 0033, VERIFICATION-MODEL.md sections 5.1 and 6, ARCHITECTURE.md
(`Equiv.Cli`), `docs/runs/2026-10-01-timeout-budget.md`, `docs/runs/2026-10-02-pair-time.md`,
`src/Equiv.Core/Configuration/EquivConfig.cs`, `src/Equiv.Cli/CompareCommand.cs`,
`src/Equiv.Verify.Z3/Z3Backend.cs`, `.claude/skills/equiv-release`.

## Design
- `EquivConfig` gains `Mode` (`Thorough`, `Quick`) and `Escalation` (bound, resource limit,
  timeout), and remembers which of `bound`, `resourceLimit`, `timeoutMs` the file set, so a mode's
  values apply only where nothing was set. `DefaultResourceLimit` becomes 2,000,000.
- `VerificationOptions` gains what the backend must know: whether rung 5's local proposer runs, and
  whether a `timeout` Unknown gets failure refinement. `Z3Backend.Verify` reads them where it
  decides today (`Reason: not UnknownReason.Timeout`).
- `CompareCommand` runs the passes. After `Verified`: the budget pass over each result that is
  Unknown and whose `ladderTrace` holds a step that hit a budget, or whose pair has a loop or a
  self-call; then the IL pass; then `WithAssumptions`, `WithContracts` (thorough only), `Executed`.
  One function decides which of two results stands (ADR 0049 decision 2) and is the only place that
  rule lives.
- IL pass: the frontend keeps both lowerings of a pair that meets ADR 0039's condition
  (`ProcedurePair` carries the IL bodies beside the IOperation ones) when the mode is thorough.
  `--il-fallback` with `--mode quick` keeps today's replacement.
- Each later pass is a phase of the run log (`budget`, `il`), weighed as `verify` is.

Pitfalls.
- A crash in a later pass must not reach ADR 0023's exit 5: the earlier result stands, with a
  warning on stderr, as `UnderContracts` does today.
- The budget pass must not ask with less than the first pass when the config sets a large
  `resourceLimit`; when the first pass's values already meet the escalation's and the bound is not
  larger, there is no budget pass.
- `--execute` and `--invariant-model` are never turned on by a mode.
- Sample and fixture tests pinned to 5,000,000 are pinned on purpose (P2-050's `loop-fusion` margin).
  Rung 4 keeps ten times the limit of the pass it runs in.

## Acceptance criteria (all must hold; nothing beyond them)
1. `compare --mode thorough|quick`, config key `mode`, MCP `compare` input `mode` and `action.yml`
   input `mode` select the mode; any other value is exit 3 (a tool error over MCP). The command line
   wins over the config, and the default is `thorough`.
2. Each cell of ADR 0049's table holds, shown by a test per row that names the mode and the step
   that did or did not run.
3. Explicit `bound`, `resourceLimit`, `timeoutMs` and `--resource-limit` replace the first pass's
   values in either mode, and config `escalation` replaces the budget pass's. A value that is not a
   positive integer is the existing diagnostic or exit 3.
4. A later pass's result replaces the earlier one exactly as ADR 0049 decision 2 says. A property
   test (CsCheck) over generated pairs of results: the chosen result is never Unknown when either
   is decided, and never the later one when the earlier is decided.
5. On every sample under `samples/`, each result that is Equivalent or Divergent with `--mode quick`
   has the same rule id with `--mode thorough` (an integration test).
6. `run.properties.mode` and `properties.decidedBy` are written as ADR 0049 decision 6 says, with a
   Verify snapshot for one run in each mode. Fingerprints, rule ids and exit codes are unchanged for
   a result both modes decide alike. A `--baseline` whose `run.properties.mode.name` differs gives one
   stderr warning.
7. Thorough without `--execute` prints one stderr note that Unknowns were not tested. With
   `--execute`, quick replays Divergents and tests no Unknown.
8. A `timeout` Unknown in thorough carries `properties.failureRefinement`; in quick it does not.
   ADR 0037's taint and opaque rules are unchanged, pinned by the existing P1-013 tests run in both
   modes.
9. A later pass that throws leaves the earlier result, writes a warning, and the run's exit code is
   what it would have been without that pass.
10. `tools/corpus/corpus.ps1` takes `-Mode`, passes it through, and `SUMMARY.md` names it.
11. Through `equiv-corpus-run` on `gitextensions-8522`, one `full` run in each mode. Notes records,
    per mode: phase times, verdict counts, Unknowns by reason, and for thorough the results each
    later pass decided, by outcome; and the number of results that today's default decides and
    quick's first pass does not. No result decided in quick is Unknown in thorough.
12. ARCHITECTURE.md (`Equiv.Cli`, MCP) and VERIFICATION-MODEL.md sections 5.1 and 6 describe the
    modes, with ADR 0049's table in section 6. ARCHITECTURE.md's `--bound` and `--timeout-ms`, which
    `compare` does not have, are removed from its option list. README's usage shows `--mode`.
13. The PR's final commit carries `Release: minor` beside `Ticket: P1-032`, and the PR body says the
    default behaviour changed.

## Files
`src/Equiv.Core/Configuration/EquivConfig.cs`, `EquivConfigLoader.cs`, `VerificationOptions.cs`,
`src/Equiv.Core/Matching/` (`ProcedurePair`), `src/Equiv.Core/Reporting/` (run and result
properties), `src/Equiv.Cli/CompareCommand.cs`, `CompareOptions.cs`, the MCP `compare` tool,
`src/Equiv.Verify.Z3/Z3Backend.cs`, `src/Equiv.Frontend.CSharp/` (keeping both lowerings), their
tests, `action.yml`, `tools/corpus/corpus.ps1`, `docs/ARCHITECTURE.md`,
`docs/VERIFICATION-MODEL.md`, `README.md`.

## Tests
`Mode_DefaultsToThorough`, `Mode_CommandLineWinsOverConfig`, `Mode_UnknownValue_IsUsageError`,
`Quick_RunsNoLaterPass`, `Quick_SkipsContractsPass`, `Quick_Execute_TestsNoUnknown`,
`Thorough_BudgetPass_OnlyOnUnknownsThatHitABudgetOrLoop`, `Thorough_IlPass_OnlyOnUnknowns`,
`Thorough_TimeoutUnknown_CarriesFailureRefinement`, `LaterPass_NeverReplacesADecidedResult`
(property), `LaterPass_Crash_KeepsEarlierResult`, `ExplicitBudget_WinsOverMode`,
`Escalation_NeverBelowFirstPass`, `Mode_NeverTurnsOnExecuteOrInvariantModel`,
`BaselineFromOtherMode_Warns`, the two run snapshots, and criterion 5's integration test.

## Size guard
If keeping both lowerings on `ProcedurePair` needs changes outside `src/Equiv.Frontend.CSharp/` and
the files listed: stop, file `P1-nnn` for the IL pass with what you found, and finish this ticket
with the IL row of the table absent in thorough (and say so in VERIFICATION-MODEL.md). Verifying
pairs in parallel, a third mode, or changing any value in ADR 0049's table: stop and file a ticket.

## Out of scope
Parallel verification. P1-030 and P1-031's techniques (each adds its own row). Asking ADR 0037's
queries on `unbound` Unknowns. Fixing P2-078 (criterion 9 makes its crash harmless in the IL pass).

## Notes
- Decided by the user on 2026-10-04 and recorded as ADR 0049 the same day.
- The escalation `bound` of 8 is not measured; criterion 11's thorough run is its first measurement.
