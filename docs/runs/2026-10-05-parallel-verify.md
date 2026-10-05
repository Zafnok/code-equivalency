# P2-077 parallel verify: gitextensions-8522 (2026-10-05)

Question: does verifying matched pairs on several threads give the results of one thread, and how much
sooner?

**Answer: on four threads the run takes 2,815 s against 7,935 s, a factor of 2.8, and no query runs slower
than it does alone. On 24 threads, this box's processor count, it takes 5,088 s, slower than on four: the
workers wait on something they share, and each query that answers takes 4.2 times as long at the median.
Two results of 13,742 differ from the one-thread run on four threads and three on 24. Every one of them is
a query the resource limit ends in some runs and not in others, which two runs on one thread also give.
Criterion 6 keeps the default at one thread while any result differs, so `jobs` defaults to 1 and
`--jobs 4` is a choice. P2-132 owns the cause of the waiting and the default.**

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f.
- Build: this branch at `7a6b8af1`, Release. Each run is `equiv compare` at the default config
  (`resourceLimit` 5,000,000, `timeoutMs` 60,000) with `--verbosity debug`, in `full` mode, with `--jobs 1`,
  `--jobs 4`, or no `--jobs` (24 threads at that commit, where the default was the processor count).
- Machine: one Windows box, 24 cores without hyper-threading, 63 GB, shared with one to three corpus runs and
  a solver spike of other sessions. The `--jobs 4` run and the second `--jobs 1` run ran side by side.
- Two `--jobs 1` runs. The first was stopped by the session's two-hour limit on a background command six
  minutes before its end, after its `verify` phase and 663 of 789 `contracts` items, and wrote no SARIF. Its
  progress log is used below where a second one-thread run is wanted. The second ran to its end and is the
  `--jobs 1` run of every table.
- A fifth run is a probe, not a criterion: 24 threads with the server garbage collector (`DOTNET_gcServer=1`).
- On `n` threads every query has `n` times `timeoutMs` on the clock (`PairWorkers.Sharing`).
- Peak working set is the process's, read every five seconds.

## Times and memory

| run | total | lower | verify | contracts | peak working set |
|---|---|---|---|---|---|
| `--jobs 1` | 7,935 s | 96 s | 5,377 s | 2,436 s | 6,484 MB |
| `--jobs 4` | 2,815 s | 97 s | 1,932 s | 758 s | 7,616 MB |
| 24 threads | 5,088 s | 197 s | 3,991 s | 844 s | 9,245 MB |
| 24 threads, server collector (probe) | 3,135 s | 101 s | 2,398 s | 608 s | 9,595 MB |

The peak working set at `--jobs 4` is 7.6 GB, under the 12 GB criterion 6 allows on a 16 GB hosted runner.
Most of it is the two loaded solutions, which the one-thread run holds too.

## Results (criterion 6)

Rule id, `unknownReason` and `proofMethod` of every result, against the `--jobs 1` run:

| run | results | same | differ | rungs ended by the resource limit | rungs ended by the backstop |
|---|---|---|---|---|---|
| `--jobs 1` | 13,742 | | | 240 | 22 |
| `--jobs 4` | 13,742 | 13,740 | 2 | 258 | 3 |
| 24 threads | 13,742 | 13,739 | 3 | 244 | 18 |
| 24 threads, server collector (probe) | 13,742 | 13,741 | 1 | 261 | 1 |

No run has an unverified pair or a pair-level notification. The count of rungs the backstop ended
(`run.properties.queryEndings.wallClock`) is lower on several threads than on one in every run, as criterion
6 asks: with more time on the clock, a query the 60 s backstop ends on one thread reaches the resource limit.

The results that differ:

| pair | `--jobs 1` | `--jobs 4` | 24 threads |
|---|---|---|---|
| `ResourceManager.Translator::GetTranslation(string)` | EQ003 `timeout` (resource limit) | EQ003 `unaligned-loop` | EQ003 `unaligned-loop` |
| `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` | EQ003 `timeout` (resource limit) | EQ006 | same as `--jobs 1` |
| `GitCommandsTests.CommitMessageManagerTests::AmendState_should_be_false_if_file_contains(string)` | EQ003 `timeout` (resource limit) | same as `--jobs 1` | EQ006 |
| `NetSpell.SpellChecker.Spelling::DeleteWord()` | EQ003 `abstraction` | same as `--jobs 1` | EQ003 `timeout` (resource limit) |

In every row one run's query ran out of the resource limit and the other's answered. None was ended by the
wall-clock backstop, so none is a query that threads cut short. `AmendState_should_be_false_if_file_contains`
is the pair P2-076 found decided in 17 to 40 of 200 runs of one build. This is P2-100's subject: the solver's
path through one query depends on when the garbage collector released terms.

Two runs on one thread differ the same way. The first `--jobs 1` run has no SARIF, so the comparison is of
the `verify` phase's outcome for each pair (equivalent, divergent, unknown) in the progress logs:

| compared | pairs whose `verify` outcome differs |
|---|---|
| first `--jobs 1` and second `--jobs 1` | 1 (`GitCommands.GitRefName::GetRemoteName`, divergent then unknown) |
| second `--jobs 1` and `--jobs 4` | 1 |
| second `--jobs 1` and 24 threads | 1 |

And of the solver checks the two one-thread logs have in common (same pair, phase, query and position), 7 of
3,607 answer differently; against the second one-thread run, 5 of the `--jobs 4` run's do and 7 of the
24-thread run's. Threads add nothing to what one thread already does.

By criterion 6 one differing result keeps the default at 1, and it does. A P2 ticket is filed, P2-132.

## Threads never shorten a query (criterion 4)

The time of each solver check that answered sat or unsat in both runs and took at least 1 s on one thread,
as a ratio to its time on one thread:

| run | checks | median | 90th percentile | largest | slowest answer |
|---|---|---|---|---|---|
| first `--jobs 1` (noise) | 90 | 0.98 | 1.30 | 1.75 | |
| `--jobs 4` | 93 | 0.99 | 1.19 | 1.64 | 14.6 s |
| 24 threads | 89 | 4.24 | 9.23 | 28.48 | 276.5 s |
| 24 threads, server collector (probe) | 93 | 1.86 | 3.67 | 33.07 | 148.3 s |

The slowest answer on one thread took 53.3 s. On 24 threads three checks answered after more than 60 s
(99.5 s, 114.7 s and 276.5 s). With the backstop left at 60 s each would have been a timeout that one thread
does not give, which is the loss criterion 4 forbids; with 24 times the backstop none was. A ratio of 28 is
above 24, so the multiplier is about right on this box and not generous.

The multiplier has a price. A query that the backstop ends at 60 s on one thread runs on to its resource
limit, and a phase cannot end before its slowest pair. On 24 threads 171 `verify` checks ran past 60 s, 18
of them to the 24-minute backstop, and took 69,500 s between them; the slowest pair took 2,947 s. On four
threads 34 pairs ran past 60 s and the slowest took 387 s.

## Why 24 threads are slower than four

The `verify` phase's work, summed over pairs, by stage:

| stage | `--jobs 1` (first run) | 24 threads | ratio |
|---|---|---|---|
| checks answering sat | 218 s | 1,377 s | 6.3 |
| checks answering unsat | 45 s | 825 s | 18.6 |
| checks giving up | 4,794 s | 74,456 s | 15.5 (most of it the longer backstop) |
| encode | 12 s | 45 s | 3.9 |
| inline | 41 s | 190 s | 4.6 |
| dispose | 59 s | 217 s | 3.7 |
| replay | 64 s | 3,343 s | 52.2 |

Every stage is slower, the ones in managed code and the ones inside Z3. The processors were not the limit:
over the phase the process used four cores of 24 on average. A stack report of the probe run
(`dotnet-stack report`, once, 20 minutes in) shows 16 of the 24 workers inside the native call under
`SecondSolver.Check` (building or checking a rung 1 query) while the thread list shows four to seven threads
running and the rest waiting. So the workers wait on something shared inside or below the native solver.
The server garbage collector halves the median slowdown and leaves the largest where it was, so the
collector is part of it and not all of it. What the rest is has not been measured: native stacks are needed,
and that is P2-132's first criterion.

## What changes
- `compare --jobs <n>` and the config's `jobs` verify up to `n` pairs at once; the default is 1.
- `run.properties.queryEndings` counts the rungs each limit ended.
- The corpus skill says a verdict run may pass `--jobs 4`.
- P2-131: the backend counts how every query ended (this run counts timed-out rungs from their detail text).
- P2-132: find what the workers wait on past four threads, and raise the default.
