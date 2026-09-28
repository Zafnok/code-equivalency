# M4-014 Backend progress: rung and solver-query timings at `debug`
Status: done (PR #244)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-012

## Goal
When a pair is slow, the debug log says where the time went. `Z3Backend` and `LoopLadder` write
one `Detail` line per strategy or rung tried. Each line names the rung (`direct`, `k-induction`,
`lockstep`, `spacer`, `llm-invariant`, and so on, using the names the code already has), how long
the rung took, and its result (`sat`, `unsat`, `unknown` or `timeout`). Everything goes through
`VerificationOptions.Log`. At `normal` nothing changes. The heartbeat from M4-012 already names a
stuck pair.

## Spec references
ADR 0038.

## Acceptance criteria (all must hold; nothing beyond them)
1. With `options.Log.IsDebug`, each rung the backend runs for a pair produces exactly one `Detail`,
   in the form `rung=<name> took=<s> result=<r>`.
2. With `IsDebug` false, the backend builds no detail string. A test checks this with a recording
   log whose `IsDebug` is false and which fails if `Detail` is called.
3. Verdicts are unchanged for every sample. The snapshot tests pass unchanged.
4. Timing uses `TimeProvider.System.GetTimestamp()` / `GetElapsedTime`. There is no `DateTime.Now`.

## Files
- `src/Equiv.Verify.Z3/Z3Backend.cs`, `LoopLadder.cs`, and the rung classes only where the result
  string is decided

## Tests
- `Equiv.Verify.Z3.Tests/BackendProgressTests.cs` (`Each_Rung_Is_One_Detail`, `No_Detail_Unless_Debug`)

## Size guard
More than 6 non-test files changed: stop and re-read.

## Out of scope
A per-pair wall-clock cap, changes to timeouts, anything verdict-affecting.

## Notes
Decision: rung 5 asks the proposer for several rounds inside one `Prove` call, so it is logged as one `rung=llm-invariant` Detail (whole call, result of the last round), not one per round. Rung names are `bounded`, `lockstep`, `k-induction`, `spacer`, `llm-invariant`. Result maps outcome: Proved=unsat, Refuted=sat, Timeout=timeout, else unknown. Logging is in `LoopLadder.Climb` only, so `Z3Backend.cs` and the rung classes are untouched; `Independently` (test-only) is not logged.
