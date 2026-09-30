# P2-077 Matched pairs are verified in parallel, with the same results as one at a time
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-050, P2-076

## Goal
`CompareCommand.Verified` verifies the matched pairs one at a time. A Git Extensions `full` run keeps one
of the box's 24 cores busy for 6.8 hours, and a hosted GitHub runner has 4 cores and a 6-hour job limit.
Each pair already gets its own Z3 `Context`, so the pairs are independent. Verify them on up to
`--jobs` threads (default: the processor count) and keep every result the same as a run with `--jobs 1`.
Results can only stay the same once budgets are deterministic (P2-050's `rlimit`). Under wall-clock
timeouts alone, contention between threads would turn decided pairs into Unknown(timeout). A timeout
also has to mean its budget (P2-076), or one slow pair still holds up the run.

## Spec references
ADR 0038 (one item in flight per phase today), ADR 0029 decision 5, ADR 0023 (a crash on one pair does
not end the run), `src/Equiv.Cli/CompareCommand.cs` (`Verified`, `WithContracts`),
`src/Equiv.Core/Configuration/EquivConfig.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `compare` accepts `--jobs <n>` (a positive integer) and `equiv.json` accepts `jobs`, with the same
   validation and precedence as `timeoutMs`. The default is `Environment.ProcessorCount`.
2. The `verify` and `contracts` phases run up to `jobs` pairs at once. Results are written in the order
   `--jobs 1` writes them, and a pair-level crash is still one `error` notification and one
   `unverified` identity (ADR 0023).
3. The progress log shows every item in flight, and `-Progress` reads it. This is a dated
   `## Clarifications` bullet on ADR 0038 (one item became several), not a new ADR.
4. A property test over generated pairs (`tests/Equiv.Cli.Tests`, CsCheck) shows that `--jobs 1` and
   `--jobs 4` give the same SARIF once timing properties are removed.
5. On `gitextensions-8522`, a `full` run with the default `jobs` gives the same rule id and
   `unknownReason` for every result as a `--jobs 1` run at the same commit.
   `docs/runs/<date>-parallel-verify.md` gives both wall-clock times and any result that differs. If one
   differs, the default stays 1 and a P2 ticket is filed.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/CompareOptions.cs`, `src/Equiv.Core/Configuration/EquivConfig.cs`,
the run-log implementation, `tools/corpus/corpus.ps1` (`-Progress`), their tests and snapshots,
`docs/adr/0038-progress-log-with-phase-clocks.md` (Clarifications only), `docs/runs/<date>-parallel-verify.md`.

## Tests
`CompareCommandTests.JobsOptionIsValidated`, `CompareCommandTests.ParallelResultsKeepTheirOrder`,
`CompareCommandTests.ParallelCrashIsOnePairOnly`, `ParallelVerifyProperties.JobsDoNotChangeResults`.

## Size guard
Any change inside `src/Equiv.Verify.Z3/` beyond making shared state thread-safe is a finding. File a
ticket, not a fix here.

## Out of scope
Parallel lowering (lowering takes about 2 minutes). Distributing pairs across machines. Caching verdicts
between runs.

## Notes
- Found 2026-09-30 while P1-018's runs were in progress. See P2-076 for where the sequential time goes.
