# P2-077 Matched pairs are verified in parallel, with the same results as one at a time
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-050, P2-076

## Goal
`CompareCommand.Verified` verifies the matched pairs one at a time. A Git Extensions `full` run keeps one
of the box's 24 cores busy for 6.8 hours, and a hosted GitHub runner has 4 cores and a 6-hour job limit.
Each pair already gets its own Z3 `Context`, so the pairs are independent. Verify them on up to
`--jobs` threads (default: the processor count) and keep every result the same as a run with `--jobs 1`.
Results can only stay the same once budgets are deterministic (P2-050's `rlimit`). Under wall-clock
timeouts alone, contention between threads would turn decided pairs into Unknown(timeout). So no
query may end sooner under `--jobs n` than it does under `--jobs 1`. P2-076 comes first because it
removes the hours one pair spends outside the solver, which no number of threads shortens.

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
4. Threads never shorten a query. With `--jobs` above 1, the deterministic `rlimit` is what ends a
   query, and the wall-clock backstop (P2-050) is scaled or measured so that contention cannot make
   it fire first. The run counts queries ended by `rlimit` and by the backstop
   (`run.properties.queryEndings`), and a test shows that a query which finishes under `--jobs 1`
   finishes under `--jobs 4` on a machine with one busy core.
5. A property test over generated pairs (`tests/Equiv.Cli.Tests`, CsCheck) shows that `--jobs 1` and
   `--jobs 4` give the same SARIF once timing properties are removed.
6. On `gitextensions-8522`, a `full` run with the default `jobs` gives the same rule id,
   `unknownReason` and `proofMethod` for every result as a `--jobs 1` run at the same commit, and
   its count of queries ended by the backstop is no higher. `docs/runs/<date>-parallel-verify.md`
   gives both wall-clock times, both peak working sets and any result that differs. If one result
   differs, or the peak working set at `--jobs 4` is over 12 GB (a hosted runner has 16), the default
   stays 1 and a P2 ticket is filed.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/CompareOptions.cs`, `src/Equiv.Core/Configuration/EquivConfig.cs`,
the run-log implementation, `tools/corpus/corpus.ps1` (`-Progress`), their tests and snapshots,
`docs/adr/0038-progress-log-with-phase-clocks.md` (Clarifications only), `docs/runs/<date>-parallel-verify.md`.

## Tests
`CompareCommandTests.JobsOptionIsValidated`, `CompareCommandTests.ParallelResultsKeepTheirOrder`,
`CompareCommandTests.ParallelCrashIsOnePairOnly`, `CompareCommandTests.JobsDoNotShortenAQuery`,
`ParallelVerifyProperties.JobsDoNotChangeResults`.

## Size guard
Any change inside `src/Equiv.Verify.Z3/` beyond making shared state thread-safe is a finding. File a
ticket, not a fix here.

## Out of scope
Parallel lowering (lowering takes about 2 minutes). Distributing pairs across machines. Caching verdicts
between runs. Any cap on a rung, a pair or a run.

## Notes
- Found 2026-09-30 while P1-018's runs were in progress. See P2-076 for where the sequential time goes.
- Decision: how threads are kept from shortening a query -> a phase that verifies on `n` threads gives the backend
  `n` times `timeoutMs` (`PairWorkers.Sharing`), `n` being the lesser of `jobs` and the pairs of the phase that reach
  the backend. A fair scheduler gives each of `n` threads at least a share of one processor, so a query that ends
  within the backstop alone ends within `n` times it. Alternatives: the backstop times `jobs / processors` (leaves
  out two threads on one core's two hardware threads, and gives a different SARIF on a machine with fewer cores), a
  backstop measured in the thread's processor time (a change inside `Equiv.Verify.Z3`, which the size guard keeps
  out). Rule: 4.
- Decision: where `queryEndings` is counted -> in `Equiv.Cli`, from the ladders of the run's results: a query a limit
  ends times its rung out, and the rung's detail names the limit (P2-050 criterion 4). So it counts the timed-out
  rungs of the results the run reports, by limit. A query outside a rung (reachable opaques, failure refinement,
  contract search), and the ladder of a contract attempt that proved nothing, are not counted. Alternatives: a
  counter the backend reports through `VerificationOptions` (a change inside `Equiv.Verify.Z3` that is not thread
  safety; the size guard makes it a ticket, P2-131). Rule: 4.
- Decision: `queryEndings` is written on every run that verifies, zeros included, and not on `--lower-only` -> as
  `unknownByScope` is. Alternatives: only when a count is not zero (a missing key would then mean either zero or an
  older `equiv`). Rule: 3.
- Decision: how the log tells items in flight apart -> an item belongs to the thread that started it
  (`ChannelRunLog` keeps each thread's item), so `IRunLog` keeps its five calls and no caller or test double changes.
  Alternatives: an identity parameter on `ItemDone` and `Detail`, a per-item log object handed to the backend.
  Rule: 4.
- Decision: a `detail:` line ends in ` item=<identity>` when its thread has an item in flight -> the stage lines of
  pairs verified at once interleave, and P2-076's per-pair stage totals need to know whose each is. At the end of
  the line because an identity holds spaces and brackets. Alternatives: a worker number on each line. Rule: 3.
- Decision: the workers are threads of their own with a 16 MB stack, and one worker runs on the calling thread ->
  the solver recurses on the thread that calls it, and a new thread's default stack is smaller than the main
  thread's on Linux, so a pair that fits under `--jobs 1` must fit on a worker. Alternatives: `Parallel.For`
  (thread-pool threads, default stack, slow to reach the degree asked for when every body blocks). Rule: 4.
- Decision: `EquivConfig.Jobs` is an `int` defaulting to `Environment.ProcessorCount` -> the loader validates it as
  it does `timeoutMs`. Alternatives: a nullable with the default applied in the CLI. Rule: 4.
- Decision: a pair's `error:` line and a contract step's `warning:` line are written when the phase ends, in the
  pairs' order -> lines written from the workers would be in the order the pairs finished. Rule: 3.
- Observed: `Equiv.Verify.Z3` and `Equiv.Verify.Cvc5` needed no change. Their static state is three frozen
  dictionaries, a `SearchValues`, a shared `HttpClient` and a `Lazy` prompt template, all safe to share, and every
  query builds its own `Context`.
- Observed: the wall-clock detail names the backstop the query had (`wall-clock limit 240000 ms hit` on four
  threads), and the detail is part of `resultFingerprint/v1`. So a result the backstop ended is `new` against a
  baseline written with another number of threads. Such a result already differs between runs (P2-050's notes);
  the rule id and the reason do not change.
- Decision: the default `jobs` is 1, not the processor count -> criterion 6: on `gitextensions-8522` the
  `--jobs 4` run gives 2 results of 13,742 that the `--jobs 1` run does not, and the 24-thread run 3. Each is a
  query the resource limit ends in one run and not in the other, none was ended by the backstop, and two
  `--jobs 1` logs differ from each other as much (P2-100). The criterion does not ask why a result differs, so
  the default stays 1 and P2-132 is filed to raise it. Criterion 1's default is the one criterion 6 overrides.
  Alternatives: the processor count with a Deviation, as P2-076 read its own criterion 5 (24 threads are also
  slower than four on this box); four. Rule: the ticket's own rule.
- Result, criterion 6 (`docs/runs/2026-10-05-parallel-verify.md`): `--jobs 1` 7,935 s and 6,484 MB peak working
  set; `--jobs 4` 2,815 s and 7,616 MB; 24 threads 5,088 s and 9,245 MB. Rungs ended by the backstop: 22, 3 and 18.
- Observed: 24 threads are slower than four. A check that answers takes 4.2 times as long at the median on 24
  threads (0.99 on four) and up to 28 times, with four of 24 cores busy and 16 of 24 workers inside one native
  Z3 call. The server garbage collector (a probe run with `DOTNET_gcServer=1`) brings the run to 3,135 s and
  the median to 1.9, so the collector is part of it. Not fixed here: what the rest is needs native stacks
  (P2-132).
- Observed: the backstop's multiplier is needed and costly. On 24 threads three checks answered after 99 s,
  115 s and 277 s that take under 54 s alone, and 171 checks ran past 60 s, 18 of them to the 24-minute
  backstop, for 69,500 s between them.
- Observed: the first `--jobs 1` run was started as a background command of the session, whose longest time
  limit is two hours, and was stopped six minutes before its end. It wrote no SARIF. The rerun was started
  detached. A corpus run that can pass two hours must be started that way.
- Observed: `main` was red at the commit this branch started from
  (`SecondSolverLadderTests.TheSolversQueriesAreStages`, fixed on `main` since and merged in), and
  `ContractSoundnessTests.SharedFunctionUnderContractWouldBeUnsound` failed once on the Windows leg and passed
  on a rerun; it calls the backend directly and nothing it uses is changed here.
- Scoreboard unchanged: the report holds no `full` summary in the default mode and no two runs of one
  configuration with SARIF.
- Result: tickets P2-131 and P2-132.
