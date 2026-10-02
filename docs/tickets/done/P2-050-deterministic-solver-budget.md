# P2-050 Solver budgets are deterministic, and the timeout Unknowns are measured against a larger budget
Status: done (PR #328)
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
- Decision: default budget -> `resourceLimit` 5,000,000 and `timeoutMs` 60,000 (was 5,000). From
  `docs/runs/2026-10-01-timeout-budget.md`: a larger budget decides no timeout Equivalent (4x: 37 of
  172 decided, 20x: 73, all Divergent or Unknown for another reason), so the default keeps today's
  strength; 5,000,000 is where the outcomes match the 5,000 ms budget (173 and 174 timeouts against
  172), and a 60 s backstop leaves the resource limit ending about 95% of the timeouts.
  Alternatives: 2,000,000 (6 pairs weaker, and within the 1,000,000 to 3,000,000 that rung 4 needs
  for `loops/fusion`), 10,000,000 (9 pairs stronger at 6.7 times the solver time). Rule: 3.
- Decision: a Spacer query gets ten times `ResourceLimit` (`ChcEncoder.SpacerResourceScale`) -> one
  number cannot fit both engines. The `loop-fusion` sample's rung 4 proof needs 3 to 5 million units
  (proved 12 of 12 times at 5,000,000 here, 0 of 12 at 3,000,000, and lost once on CI at 5,000,000,
  which failed `Refinement_NeverChangesVerdictOrFingerprint`), and spends them in under 2 s; a
  product query takes a median 7 s for 5,000,000. Alternatives: a default of 50,000,000 for
  everything (about 20 times the solver time on timeout pairs), a second config key (not in the
  ticket). Rule: 4. The answer checks (`Solves`, `Refutes`, `DerivationInputs`) need under 100,000
  and keep the plain limit.
- Decision: how criterion 1 reran only the timeout pairs -> a throwaway harness outside the repo that
  loads both solutions through `CSharpFrontend` and calls `Z3Backend.Verify` per pair, as P1-019's
  spike did. `compare` cannot verify a subset and has no `--timeout-ms` (only `equiv mcp` and the
  config set `timeoutMs`). Alternatives: three full `compare` runs (a day each at 20x), a committed
  tool (not in Files). Rule: 4.
- Decision: `ResourceLimit` is an init property with the default, on `VerificationOptions` and
  `EquivConfig`, not a positional parameter -> the existing call sites that build either keep
  compiling and get the default. Alternatives: a fourth positional parameter. Rule: 4.
- Decision: which limit was hit is read from Z3's reason for giving up -> a solver's timer leaves
  `timeout` and an exhausted `rlimit` leaves `canceled` (tactic solver) or `max. resource limit
  exceeded`; a fixedpoint's timer leaves `canceled`. Alternatives: comparing the `rlimit count`
  statistic with the limit (it is per context, not per check). Rule: 1.
- Decision: the usage error for a value that is not positive names all three options (`bound,
  timeoutMs and resourceLimit must be positive integers`) -> one check, one message. Alternatives: a
  second message for `resourceLimit` alone. Rule: 4.
- Decision: `ChcEncoder`'s `Query`, `Solves`, `Refutes` and `DerivationInputs` take the
  `VerificationOptions` in place of `uint timeoutMs`. Rule: 4.
- Observed: the timeout detail is part of `resultFingerprint/v1` (reason and detail), and criterion 4
  changes its text, so a baseline written before this PR reports its timeout Unknowns as `new` once.
  The scheme and the rule id are unchanged.
- Observed: Z3 counts `rlimit` in units whose cost in time varies about a hundredfold between
  queries (10,000,000 units: 1.7 s on `hard-multiplication`, a median 13.8 s and up to 606 s on the
  corpus pairs). A resource limit therefore costs 2.3 times the solver time of the wall-clock budget
  of the same strength, and the backstop has to be far above the median.
- Observed: `DerivationInputs` reads its solver's model without looking at the status, so a check
  that gives up there throws (as a timeout did before). `EverySolverSetsTheResourceLimit` pins the
  throw only to show the limit is set; the query is a handful of terms and has never been seen to
  give up.
- Observed: two runs at the default budget agree on 179 of 184 pairs. Four of the other five met the
  wall-clock backstop in one run. The fifth differs under the same resource limit, and the evidence
  points at the garbage collector's timing changing the solver's path (P2-082). Criterion 5 holds as
  written (a tiny limit, byte-identical SARIF), but run-to-run identity on a real pair is not there yet.
- Observed: nine pairs take 92% of the 1x time outside any budget (P2-076). The harness's two
  all-206 runs were left to finish them; their numbers are in the run file.
- Observed: ARCHITECTURE.md lists `--bound` and `--timeout-ms` for `compare`; the command has
  neither. Not changed here.
- Result: `docs/runs/2026-10-01-timeout-budget.md`; tickets P2-082 and P2-083.
