# P2-050 Solver budgets are deterministic, and the timeout Unknowns are measured against a larger budget
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
Timeouts are the third-largest Unknown reason on Git Extensions (161 of 700 in M4-007). They also
make runs unrepeatable: every Z3 query is bounded only by wall-clock `timeoutMs` (default 5000,
`Z3Backend.Query`, `ChcEncoder`), so the same inputs gave two different rule ids between the plain
and `--execute` runs (M4-007 verdict, finding 6). A baseline comparison then shows `new` results
that nothing caused, which a CI user cannot tell apart from a regression. Bound every query by Z3's
deterministic resource limit as well, keeping wall-clock as a backstop. Also measure how many of
the timeout Unknowns only need a larger budget, so the default is chosen from data.

## Spec references
VERIFICATION-MODEL.md section 6 (reasons, scope), ADR 0029 decision 5 (timeouts are per pair),
`src/Equiv.Verify.Z3/Z3Backend.cs` `Query`, `src/Equiv.Verify.Z3/ChcEncoder.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. On `gitextensions-8522`, rerun only the pairs that P2-046's run left
   Unknown(timeout), with `--timeout-ms` at 1x, 4x and 20x the default. Record in
   `docs/runs/<date>-timeout-budget.md`, for each budget: Equivalent, Divergent, still timeout,
   other Unknown, and the total wall-clock. Identities and counts only.
2. Every solver this repo creates (`Z3Backend.Query`, each `ChcEncoder` solver and fixedpoint, and
   the failure-refinement query if P1-013 has landed) sets Z3's `rlimit` from a new
   `VerificationOptions.ResourceLimit`, and keeps `timeout` as the wall-clock backstop.
3. `equiv.json` accepts `resourceLimit` (a positive integer), and `compare` accepts
   `--resource-limit`, with the same validation and precedence as `timeoutMs`. The default is chosen
   from criterion 1's data and recorded as a `Decision:` line.
4. When a query exhausts `rlimit`, the result is Unknown with reason `timeout`, exactly as today. No new
   reason and no new rule id. The detail says which limit was hit (`resource` or `wall-clock`).
5. The resource limit binds before the backstop. With a tiny `resourceLimit` and a large `timeoutMs`,
   a pair that needs solving is Unknown(timeout, `resource`). Run twice, it gives byte-identical
   results, which does not depend on machine load.
6. VERIFICATION-MODEL.md documents `resourceLimit` next to `timeoutMs`.

## Files
`src/Equiv.Core/VerificationOptions.cs`, `src/Equiv.Core/Configuration/EquivConfig.cs`,
`src/Equiv.Core/Configuration/EquivConfigLoader.cs`, `src/Equiv.Cli/CompareCommand.cs`,
`src/Equiv.Verify.Z3/Z3Backend.cs`, `src/Equiv.Verify.Z3/ChcEncoder.cs`, `docs/VERIFICATION-MODEL.md`,
`docs/runs/<date>-timeout-budget.md`, tests.

## Tests
`Z3BackendTests.EveryQuerySetsTheResourceLimit`, `ChcEncoderTests.EverySolverSetsTheResourceLimit`,
`Z3BackendTests.ResourceExhaustion_IsTimeoutUnknown`, `EquivConfigLoaderTests.ResourceLimit_IsValidated`,
`CompareCommandTests.ResourceLimitOptionOverridesConfig`,
`SamplesEndToEndTests.RepeatedRunsAreByteIdentical`.

## Size guard
A new Unknown reason, a new rule id or a changed fingerprint is a verdict-meaning change, so it
needs an ADR: stop. Changing the default `timeoutMs` belongs to criterion 3's `Decision:` line and
nowhere else.

## Out of scope
Tactic or encoding changes to make the timed-out pairs faster. If criterion 1 shows that a larger
budget does not help, write that up as a P2 ticket with the data.

## Notes
