# P2-132 Find what blocks the pair workers past four threads, and raise the default `jobs` from one
Status: in-progress
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
- Measured (criterion 1, `docs/runs/2026-10-09-jobs-scaling.md`): the workers wait on the lock of the Windows
  process heap. Z3 allocates with the C runtime's `malloc`, which is the process heap. In 32 samples of a
  `--jobs 24` run, 214 of the 215 waiting worker stacks were entering that heap's critical section from Z3's
  allocate, free or reallocate, 198 of them under `Z3_solver_check`. Ruled out as the limit: page faults (kernel
  time is 7% of the processor time), Z3's own mutexes (11 of 399 stacks, one waiting), the collector (17 of 399
  stacks wait inside the runtime), the finalizer thread (idle in 30 of its 32 stacks).
- Decision: how the heap lock is removed -> the executable's manifest asks Windows for the segment heap
  (`src/Equiv.Cli/app.manifest`, `heapType`). Alternatives: worker processes (a new component and an ADR, for a
  cause that is below Z3 and not inside it), building Z3 with another allocator (ADR 0030 rejects owning the
  build), the server collector (P2-077's probe: it halves the median and leaves the largest). Rule: 4.
- Decision: the compare mode of criterion 3's runs -> quick, the default mode (ADR 0052), whose pass is thorough's
  first pass. Alternatives: thorough (three hours a run on four threads, and nine runs were needed). Rule: 4.
- Decision: which checks the ratio is taken over -> those that answered sat or unsat in both runs and took at least
  0.1 s on one thread, 157 of them. Alternatives: at least 1 s as P2-077 had (two checks in quick mode), every
  check (most take a millisecond, and their ratio is the clock's grain). Rule: 3.
- Decision: the backstop's multiplier when the threads outnumber the processors -> 6 for each thread to a
  processor, and never more than the threads. Alternatives: 6 whatever the machine (a `--jobs 48` run on four
  cores would end a query sooner than one thread does, which criterion 5 forbids). Rule: 2.
- Result, criterion 3 (fixed build, quick mode): one thread 864 s, four 332 s, eight 272 s, twelve 241 s, 24
  221 s; peak working set 3,553, 4,238, 5,146, 5,948 and 8,404 MB. Unfixed: 342, 276, 292 and 422 s on 4, 8, 12
  and 24 threads. Largest ratio of an answered check to its time on one thread: 1.45, 2.00, 2.87 and 5.74.
- Result, criterion 4: every run gives the rule id, `unknownReason` and `proofMethod` of the one-thread run for all
  13,818 results, and no solver check of 3,179 answers differently. The pull request's build with no `--jobs` (24
  threads here) does too, in 237 s. The default `jobs` is the processor count up to 24.
- Result, criterion 5: the multiplier is 6 (5.74 rounded up). `queryEndings` is `resourceLimit` 179 and
  `wallClock` 0 in every run.
- Observed: not measured on Linux, where the manifest does nothing and glibc gives each thread an arena. This
  pair's legacy side loads only on Windows.
- Observed: not measured in thorough mode. Its later passes ask longer queries with more memory each, and the
  ratio there may be larger than 5.74.
- Observed: the manifest reaches a process only through `Equiv.Cli.exe` (the apphost, which `dotnet run` starts,
  and the single-file bundle; both checked to hold it). `dotnet Equiv.Cli.dll` runs under `dotnet.exe`'s manifest
  and keeps the old heap.
- Observed: a 24-thread run now spends 83 s of its 221 s in `lower`, which is one thread's work and out of scope
  here.
- Observed: `CompareModeTests` read the order the backend is called in and the backstop each pass gives, so its
  helper passes `Jobs = 1`.
- Scoreboard unchanged: the report holds no `full` summary of a pair and changes no count; the README's run-time
  row describes `--jobs 4` runs of three pairs at another commit.
