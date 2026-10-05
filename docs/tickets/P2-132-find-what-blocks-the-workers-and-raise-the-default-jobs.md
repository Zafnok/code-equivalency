# P2-132 Find what blocks the pair workers past four threads, and raise the default `jobs` from one
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-077, P2-100

## Goal
P2-077 verifies pairs on `--jobs` threads. On `gitextensions-8522` (`docs/runs/2026-10-05-parallel-verify.md`)
four threads take the run from 7,935 s to 2,815 s with no query slowed (the checks that answer take 0.99 times
as long at the median, 1.64 at most). Twenty-four threads on a 24-core box take 5,088 s, slower than four: the
same checks take 4.2 times as long at the median and up to 28 times, and a snapshot of the process shows 16 of
the 24 workers inside one native Z3 call with four to six threads running and four cores busy. The workers are
blocked on something they share, and nobody has found what. The server garbage collector, tried through
`DOTNET_gcServer=1`, helps the first minutes and is not the whole of it (the run file has the numbers).

Separately, `jobs` defaults to 1. P2-077's criterion 6 keeps it there while a run on several threads gives any
result a run on one does not: two or three of 13,742 did, every one a query the resource limit ends in some
runs and not in others, which two runs on one thread also give (P2-100).

Find the lock, remove it or go round it, and make the default the number of threads that measures fastest.

## Spec references
`docs/runs/2026-10-05-parallel-verify.md`, `src/Equiv.Cli/PairWorkers.cs`, `src/Equiv.Cli/Equiv.Cli.csproj`,
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`, `Check`), ticket P2-100, ADR 0030 (the Z3 build), ADR 0029
decision 5.

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. Native and managed stacks of a `--jobs 24` run of `gitextensions-8522`, sampled at least
   twenty times across the `verify` phase, name what the waiting workers wait on. Candidates to confirm or
   rule out, each with its evidence in `## Notes`: the process heap under Z3's allocations, page faults on
   memory a disposed context gave back, Z3's own globals (the symbol table, `scoped_timer`'s worker pool), the
   garbage collector suspending threads that return from a native call, the finalizer thread releasing terms.
2. The cause is removed, by the smallest change that removes it, and logged as a `Decision:` line. If it is
   inside Z3's native code, the alternative is measured too: the phase's items shared among worker processes,
   each a run of this executable on a slice of the pairs. A worker process is a new component, so that route
   stops for an ADR before any code.
3. `--jobs 4`, `--jobs 8`, `--jobs 12` and `--jobs 24` are each run in `full` mode on `gitextensions-8522` at
   one commit. `docs/runs/<date>-jobs-scaling.md` gives, for each: wall-clock time, peak working set, the
   ratio of each answered check's time to the same check's on one thread (median, 90th percentile, largest),
   and `queryEndings`.
4. The default `jobs` becomes the processor count, capped at the largest number of threads in criterion 3
   whose run is no slower than the one below it, if P2-100 has landed and the default-`jobs` run gives the
   same rule id, `unknownReason` and `proofMethod` for every result as a `--jobs 1` run at the same commit. If
   a result still differs, the default stays 1 and the differing pairs are listed with how often ten repeats
   on one thread decide them.
5. The wall-clock backstop's multiplier (`PairWorkers.Sharing`, the number of threads) is replaced by the
   largest ratio criterion 3 measured, rounded up, if that is smaller. No query ends sooner than it does on
   one thread.

## Files
`src/Equiv.Cli/PairWorkers.cs`, `src/Equiv.Cli/Equiv.Cli.csproj`, `src/Equiv.Core/Configuration/EquivConfig.cs`,
whatever criterion 1 names, their tests, `docs/VERIFICATION-MODEL.md`, `README.md`, `docs/runs/<date>-jobs-scaling.md`.

## Tests
`PairWorkersTests` for the multiplier, `EquivConfigLoaderTests.Jobs_IsValidated` for the default, and one test
named for the cause that criterion 2 removes.

## Size guard
A change to which queries are asked, to `resourceLimit` or to a verdict means the ticket has been misread: stop.

## Out of scope
Parallel lowering. Distributing pairs across machines. Caching verdicts between runs.

## Notes
- Found 2026-10-05 by P2-077's runs.
