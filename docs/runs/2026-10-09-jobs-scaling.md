# P2-132 what the pair workers wait on, and how many threads to use: gitextensions-8522 (2026-10-09)

Question: P2-077 found 24 threads slower than four, with most workers waiting inside one native Z3 call. What do
they wait on, can it be removed, and how many threads should a run use when nobody says?

**Answer: they wait on the lock of the Windows process heap. Z3 allocates with the C runtime's `malloc`, which is
the process heap, and 214 of the 215 waiting worker stacks sampled were waiting to enter that heap's critical
section from Z3's allocate, free or reallocate. The executable now asks Windows for the segment heap, which has no
such lock. With it a run on 24 threads takes 221 s where it took 422 s, each step from 1 to 4, 8, 12 and 24
threads is faster than the one before, and every run gives the result the one-thread run gives for all 13,818
results. So the default `jobs` becomes the processor count, up to 24, and the wall-clock backstop is multiplied by
at most 6 where it was multiplied by the number of threads.**

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`). 13,592 matched pairs, 13,818 results in every run.
- Build: `main` at `c8bf2024`, Release. "Unfixed" is that commit as it is. "Fixed" is that commit with this
  ticket's `src/Equiv.Cli/app.manifest`, and nothing else changed. The last run is the pull request's build,
  with the new default and the new backstop.
- Each run is `Equiv.Cli.exe compare` in `full` mode of `equiv-corpus-run`, in the default compare mode, quick
  (`bound` 3, `resourceLimit` 2,000,000, `timeoutMs` 60,000), with `--verbosity debug`. The first run was started
  as `dotnet Equiv.Cli.dll`. Runs went one after another, never two at once.
- Machine: one Windows 11 box, 24 cores without hyper-threading, 63 GB, running nothing else.
- Peak working set is the process's, read every five seconds.
- Thorough mode was not run: it takes three hours on four threads on this pair
  (`docs/runs/2026-10-08-thorough-budgets.md`), and its first pass is the pass measured here.

## What the workers wait on (criterion 1)

The unfixed build was run with `--jobs 24` and every thread's native stack was read 32 times, eight seconds
apart, from the first `verify` item to the last. The sampler suspends one thread at a time, reads its context and
walks its stack with `dbghelp`'s `StackWalk64`. Symbols are the exported names only, so a frame inside `ntdll` is
named by the nearest export and a frame inside `libz3` by its address. `dotnet-stack report` gave the managed
stacks on every third sample.

Of 399 stacks of worker threads:

| the worker is | stacks |
|---|---|
| waiting to enter the process heap's critical section (`NtWaitForAlertByThreadId` under `RtlEnterCriticalSection`, under `RtlAllocateHeap`, `RtlFreeHeap` or `RtlReAllocateHeap`) | 214 |
| running inside `libz3` | 103 |
| running inside the heap, holding or about to take the lock | about 30 |
| inside the runtime, waiting on an event | 17 |
| in `_Mtx_lock` on a mutex of Z3's own | 11, one of them waiting |
| elsewhere | the rest |

Every one of the 214 enters the heap through `ucrtbase`'s `malloc`, `free` or `realloc`, called from one of three
addresses in `libz3` (`+0xD14A5B`, `+0xD14ACE`, `+0xD14E40`): Z3's own allocate, free and reallocate. 198 of them
are under `Z3_solver_check`. The managed stacks agree: the workers are in `SolverQuery.Check`, most often
under `SecondSolver.Check` or `LockstepInduction.Prove`.

In the first five samples up to 11 of the 24 workers wait on the lock. From the sixth, 40 s into the phase, 13 to
18 do in every sample while more than 12 workers have work, and most of those left do until the last few pairs.

The candidates the ticket names:

| candidate | finding | evidence |
|---|---|---|
| the process heap under Z3's allocations | the cause | 214 of 215 waiting stacks; none in the fixed build's 242 |
| page faults on memory a disposed context gave back | not the limit | kernel time is 103 s of 1,524 s of processor time in the unfixed run and 66 s of 1,305 s in the fixed one; no waiting stack is in a virtual-memory call |
| Z3's own globals (symbol table, `scoped_timer`'s pool) | small | 11 of 399 stacks in a Z3 mutex, one waiting; 13 of 242 in the fixed build |
| the collector suspending threads that return from a native call | small | 17 of 399 stacks wait on an event inside the runtime; P2-077's server-collector probe helped because it changed how often the workers reach the heap together, not because the collector was the lock |
| the finalizer thread releasing terms | not it | of its 32 stacks 30 are idle and one is on the same heap lock, freeing |

## The change (criterion 2)

The lock is below Z3, in the heap Z3's `malloc` reaches, and a process chooses its heap's kind only when it
starts. `src/Equiv.Cli/app.manifest` asks for the segment heap (`heapType`), and the SDK copies the manifest into
the apphost and into the single-file bundle. Nothing in Z3 and no query changes. A sampled run of the fixed build
on 24 threads has no worker stack in a critical section in 19 samples: 195 of 242 worker stacks are running, and
all 24 workers run until the phase is down to its last few pairs.

The manifest does nothing on Linux, where glibc gives each thread an arena of its own. No Linux run was made:
this pair's legacy side loads only on Windows.

Since the cause is not inside Z3's code and is removed, worker processes were not measured.

## Scaling (criterion 3)

Fixed build:

| threads | wall-clock | `verify` | peak working set | ratio: median | 90th percentile | largest | `queryEndings` |
|---|---|---|---|---|---|---|---|
| 1 | 864 s | 751 s | 3,553 MB | | | | resourceLimit 179, wallClock 0 |
| 4 | 332 s | 215 s | 4,238 MB | 1.13 | 1.24 | 1.45 | resourceLimit 179, wallClock 0 |
| 8 | 272 s | 158 s | 5,146 MB | 1.37 | 1.58 | 2.00 | resourceLimit 179, wallClock 0 |
| 12 | 241 s | 127 s | 5,948 MB | 1.38 | 1.82 | 2.87 | resourceLimit 179, wallClock 0 |
| 24 | 221 s | 107 s | 8,404 MB | 1.83 | 2.99 | 5.74 | resourceLimit 179, wallClock 0 |
| default (24), the pull request's build | 237 s | 120 s | 8,511 MB | 2.09 | 2.79 | 5.13 | resourceLimit 179, wallClock 0 |

Unfixed build, for comparison:

| threads | wall-clock | `verify` | peak working set | ratio: median | 90th percentile | largest |
|---|---|---|---|---|---|---|
| 4 | 342 s | 225 s | 4,120 MB | 1.18 | 1.34 | 1.69 |
| 8 | 276 s | 164 s | 4,981 MB | 1.34 | 1.62 | 2.38 |
| 12 | 292 s | 178 s | 5,510 MB | 1.53 | 2.50 | 3.93 |
| 24 | 422 s | 270 s | 7,952 MB | 2.82 | 5.84 | 11.83 |

The ratio is of each solver check's time to the same check's time on one thread (same pair, query and position),
over the 157 checks that answered sat or unsat in both runs and took at least 0.1 s on one thread. In quick mode
only two answered checks take a second or more on one thread; their ratios on 4, 8, 12 and 24 threads of the fixed
build are 1.21, 1.62, 1.52 and 1.95.

The 78 checks that the resource limit ended after a second or more slow down less: on 24 threads of the fixed
build 1.51 at the median, 2.00 at the 90th percentile and 2.79 at most (unfixed: 2.42, 8.83 and 15.21). The
longest check of any run of the fixed build takes 59.2 s on one thread and 64.7 s on 24. On 24 threads of the
unfixed build it takes 229.3 s.

A check still takes about twice as long beside 23 others. The threads share memory and caches, and the run on 24
threads spends 1,305 s of processor time where the run on one spends 933 s.

What is left of a 24-thread run: `lower`, which is one thread's work and takes 83 s, and the tail of `verify`,
which cannot end before its slowest pair. All 24 workers are busy for the first 55 s of `verify`; eight or fewer
are for the rest.

## Results (criterion 4)

Rule id, `unknownReason` and `proofMethod` of every result, against the one-thread run of the fixed build:

| run | results | differ | solver checks in common | checks whose answer differs |
|---|---|---|---|---|
| fixed, 4 threads | 13,818 | 0 | 3,179 | 0 |
| fixed, 8 threads | 13,818 | 0 | 3,179 | 0 |
| fixed, 12 threads | 13,818 | 0 | 3,179 | 0 |
| fixed, 24 threads | 13,818 | 0 | 3,179 | 0 |
| the pull request's build, no `--jobs` (24 threads) | 13,818 | 0 | 3,179 | 0 |
| unfixed, 4, 8, 12 and 24 threads | 13,818 each | 0 | 3,179 | 0 |

P2-100 has landed, and no check answers differently in any run. Each run of the fixed build is faster than the
one below it, so the cap is 24, the most threads measured, and the default `jobs` is the processor count up to 24.

## The backstop (criterion 5)

The largest ratio measured is 5.74, on 24 threads. Rounded up it is 6, which is less than 24, so a phase on `n`
threads gives each query `timeoutMs` times the lesser of `n` and 6. No run here has a query the backstop ended,
on any number of threads, and the longest check on 24 threads, 64.7 s, is far inside six times 60 s.

A run told to use more threads than the machine has processors gets 6 times the threads to a processor, and
never more than `n` times: threads that share a processor each get a share of it, which is what P2-077's
multiplier was for.

## What changes
- `src/Equiv.Cli/app.manifest`: the Windows executable asks for the segment heap.
- The default `jobs` is the processor count, up to 24 (`EquivConfig.MaxDefaultJobs`).
- `PairWorkers.Sharing` multiplies the backstop by at most 6 (`PairWorkers.Slowdown`) for each thread to a
  processor.
- The corpus skill no longer says to pass `--jobs 4`.
