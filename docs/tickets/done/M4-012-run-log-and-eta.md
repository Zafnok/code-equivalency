# M4-012 Run log: `--verbosity`, `--log`, phase clocks, heartbeat and ETA, written off the pipeline thread
Status: done (PR #232)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-004
(ADR 0038, accepted 2026-09-27. This ticket does not block M4-007 and must not be merged into a
build that an in-progress corpus run is using.)

## Goal
`equiv compare` reports where it is and how long is left, without slowing down the pipeline. This
ticket adds the Core contract (`IRunLog`, `NullRunLog`, `EtaEstimator`), the CLI's channel-backed
writer, the two options, and the phase events that `CompareCommand` can emit by itself: `load`
(around `Analyze`, as one item until M4-013), `verify` (one item per lowered pair, weighted),
`execute`, and `write`. The frontend's and the backend's own events are M4-013 and M4-014. The
ticket is about 8 source files and 5 test files.

## Spec references
ADR 0038 (the whole decision); ADR 0033 (stdout is reserved); ADR 0027 (measure before optimising).

## Design
- **Contract (`Equiv.Core/Progress/`).** `public interface IRunLog { void Phase(string name, int total, long totalWeight, PhaseBound? bound = null); void Item(string identity, long weight); void ItemDone(string outcome); void Detail(string text); void PhaseDone(); bool IsDebug { get; } }`.
  `NullRunLog.Instance` does nothing. Callers guard costly `Detail` strings with `IsDebug`.
  `VerificationOptions` gains `public IRunLog Log { get; init; } = NullRunLog.Instance;`. It takes
  no part in the record's equality (override `Equals` as `EquivConfig` does, or keep it out
  another way; log a `Decision:`).
- **Weight (`Equiv.Core/Progress/PairWeight.cs`).** A pure function of the two `IrProcedure`s and
  how the pair will be decided. Unbound, async mismatch or congruent: 1. Otherwise: the instruction
  count of both sides, times `LoopFactor` (start at 20) when either side has a back edge. Use
  constants, not config.
- **ETA (`Equiv.Core/Progress/EtaEstimator.cs`).** Pure, and takes timestamps from a
  `TimeProvider`. `Estimate(elapsed, doneWeight, totalWeight, doneItems, ceiling)` returns null until
  `doneWeight >= 5%` of the total or 20 items are done. After that it returns
  `elapsed / doneWeight × (totalWeight − doneWeight)`. `WorstCase(remainingSolverPairs, timeoutMs,
  rungs)` is the bound printed beside it. It records the ETA it gave at 25%, 50% and 75% of the
  weight, to print at `PhaseDone`.
- **Writer (`Equiv.Cli/Progress/ChannelRunLog.cs`).** A bounded `Channel<RunEvent>` (capacity
  4096, `SingleReader = true`, `FullMode = DropWrite`), and a counter of dropped events.
  Producer calls do `Interlocked.Exchange` on a `Snapshot` (phase, index, total, current identity,
  item start timestamp) and then `TryWrite`. They never await and never lock. One consumer `Task`
  reads events and a `PeriodicTimer` together. The heartbeat period is 60 s at `normal` and 10 s
  at `debug`. On a tick it formats a line from the snapshot even when no event arrived. That line
  names the current item and its elapsed time, plus `slow` once that time passes 10× the phase's
  median item time. `Dispose` completes the channel and waits at most 2 s for the drain.
  Lines go to a `TextWriter` (stderr) and, with `--log`, to a `StreamWriter` with `AutoFlush`.
  The `TimeProvider` and both writers are constructor parameters, so tests are deterministic.
- **Line grammar.** It is fixed, so `corpus.ps1` can parse it:
  `equiv: +HH:MM:SS <phase> <done>/<total> (<pct>%) [item=<identity>] [outcome=<o>] [took=<s>] eta=<dur>|eta=? [worst=<dur>] [rate=<n>/s] [slow]`.
  A phase end is `equiv: +HH:MM:SS <phase> done in <dur>; eta@25%=<dur> eta@50%=<dur> eta@75%=<dur> dropped=<n>`,
  with `?` for a quarter that got no estimate, and a detail is `equiv: +HH:MM:SS <phase> detail: <text>`. `<dur>` is
  `HH:MM:SS.fff`, `took` is seconds with three decimals and `rate` has one decimal, all in the invariant culture.
- **Levels.** `quiet` writes nothing. `normal` writes the phase start and end lines, the
  heartbeat, and a progress line at most every 5% of weight. `debug` also writes one line per item,
  with its outcome, and every `Detail`.
- **Wiring.** `CompareCommand.Create` builds `ChannelRunLog` from the options. `Run` takes an
  `IRunLog` parameter, which tests pass as `NullRunLog.Instance`, and passes it to `Verified` and
  to `VerificationOptions.Log`. The pre-existing `note:` and `error:` lines are unchanged.

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv compare --verbosity quiet|normal|debug` (default `normal`) and `--log <path>` exist. Any
   other verbosity value is a usage error (exit 3).
2. With `normal`, a run on `samples/business-layer` writes, on stderr only, a start line and an end
   line for each of `load`, `verify` and `write` in the grammar above, and `execute` too with
   `--execute`. Stdout and the SARIF file are byte-identical to the same run with `quiet`.
3. With `--log p`, `p` holds the same lines as stderr.
4. The heartbeat is emitted by the consumer task while the producer is blocked. A test does this
   with a fake `TimeProvider` and a producer that holds an item open. It advances time past two
   periods and sees two heartbeat lines that name the item, with no `ItemDone` in between.
5. A producer call never blocks. A test fills the channel with the consumer paused, sees
   `TryWrite` return, and then sees the phase-end line report `dropped=<n>` with the exact count.
6. `EtaEstimator` has CsCheck property tests. The ETA is null below the thresholds, non-negative
   above them, exact for constant per-weight cost, and never greater than `WorstCase` when every
   item's time is at most its timeout bound.
7. `PairWeight` is 1 for unbound, async-mismatch and congruent pairs, and applies the loop factor
   exactly when a back edge exists.
8. `IRunLog` and `NullRunLog` are in `Equiv.Core`, and `ChannelRunLog` is in `Equiv.Cli`. The
   architecture tests still pass. No new NuGet package.

## Files
- `src/Equiv.Core/Progress/IRunLog.cs`, `NullRunLog.cs`, `EtaEstimator.cs`, `PairWeight.cs`
- `src/Equiv.Core/VerificationOptions.cs`
- `src/Equiv.Cli/Progress/ChannelRunLog.cs`, `RunEvent.cs`, `RunLogLine.cs` (formatting)
- `src/Equiv.Cli/CompareCommand.cs`, `CompareOptions.cs`

## Tests
- `Equiv.Core.Tests/Progress/EtaEstimatorTests.cs` (the properties in criterion 6)
- `Equiv.Core.Tests/Progress/PairWeightTests.cs`
- `Equiv.Cli.Tests/Progress/ChannelRunLogTests.cs` (`Heartbeat_WhileProducerBlocked_NamesItem`, `Full_Channel_Drops_And_Counts`, `Quiet_Writes_Nothing`, `Log_File_Mirrors_Stderr`, `PhaseDone_Reports_Eta_Checkpoints`)
- `Equiv.Cli.Tests/Progress/RunLogLineTests.cs` (the grammar, invariant culture)
- `Equiv.Cli.Tests/CompareCommandProgressTests.cs` (criteria 1 to 3 on `samples/business-layer`)

## Size guard
More than 12 source files, or any change under `src/Equiv.Frontend.*` or `src/Equiv.Verify.*` apart
from passing `NullRunLog` in existing tests: stop. Those are M4-013 and M4-014.

## Out of scope
Frontend and backend events (M4-013, M4-014). Cancellation or Ctrl-C handling. A per-pair wall-clock
cap. Parallel verification. MCP progress notifications. Any change to SARIF.

## Notes
- Decision: `VerificationOptions.Log` and equality -> left out of the existing `Equals`/`GetHashCode` override, which already lists its members by hand. Alternatives: a wrapper type with constant equality. Rule: 4.
- Decision: "instruction count" in `PairWeight` -> instructions plus one per block (its terminator) on both sides, so a pair that reaches the solver weighs at least 2 and never ties with the weight-1 pairs. Alternatives: instructions only (an empty body weighs 0 and stalls the ETA). Rule: 3.
- Decision: `EtaEstimator.Estimate` -> takes the done item count as a fourth argument (the "or 20 items" threshold needs it) and never reads a clock; the writer computes `elapsed` from `TimeProvider` timestamps. Alternatives: an estimator that owns a `TimeProvider`. Rule: 3.
- Deviation: `IRunLog.Phase` gains an optional fourth parameter, `PhaseBound? bound = null` (solver pairs, `TimeoutMs`, rungs), because the ticket's contract gives the writer no way to learn ADR 0038's worst-case inputs, which only `CompareCommand` knows once the config is loaded. The writer prints `worst=WorstCase(min(solver pairs, items left), TimeoutMs, rungs)` and clamps `eta` to it, which is what makes criterion 6's "never greater than `WorstCase`" hold when weights differ. Calls without a bound (M4-013's) are unchanged. Rungs are 1 for a loop-free pair and 5 (the ladder's rungs, `LoopLadder.Climb`) when either side has a back edge.
- Decision: the grammar's `<dur>` -> `HH:MM:SS.fff`, `took=<s>` -> seconds with three decimals, `rate=<n>/s` -> items per second with one decimal, a missing checkpoint -> `?`, all invariant culture. A debug item line adds `outcome=<o>` after `item=`, a heartbeat past 10x the median ends in ` slow`, and a `Detail` is `equiv: +HH:MM:SS <phase> detail: <text>`. `dropped=<n>` is the run's total so far. Alternatives: whole seconds (a sub-second phase reads `done in 00:00:00`). Rule: 3.
- Deviation: criterion 2's run on `samples/business-layer` is in `Equiv.Tests.Integration/CompareProgressSampleTests.cs`, not `Equiv.Cli.Tests`: loading the legacy `.csproj` needs VS Build Tools (Windows `gates` leg only), and a real solution load in `Equiv.Cli.Tests` would run under every Stryker mutant of `Equiv.Cli`. `Equiv.Cli.Tests/CompareCommandProgressTests.cs` covers criteria 1 to 3 against the fakes.
- Decision: disposal -> `ChannelRunLog` is `IDisposable`, not `IAsyncDisposable`: `Dispose` waits on the consumer for at most 2 s, and `compare`'s action stays synchronous with plain `using` declarations. An `await using` in the action left a compiler-generated resume line that coverlet reported as unhit. Alternatives: `IAsyncDisposable` with `WaitAsync`. Rule: 3.
- Decision: the consumer task makes its `PeriodicTimer` itself, before its first `await`, so the constructor returns with the timer running and it is disposed by the task that uses it. Alternatives: a timer field (CA2213 wants the log to dispose it while the task may still wait on it). Rule: 4.
- Note: the `--log` writer is `TextWriter.Null` without `--log`, so both writers are always disposed by `using`; the log itself owns neither.
- Note: on `samples/business-layer` at `debug`, `verify` takes about 0.2 s over 30 pairs (24 congruent, 6 to the solver) and `load` about 4.6 s, so the weight puts 70% of the phase in the last six items, where the time actually goes.
- Note: CI showed that `--execute`'s temp folder can fail to delete with "Access to the path 'EquivReplay1.exe' is denied" right after the replay drivers exit. That was exit 5 with no SARIF, and it hit `TestedUnknownTests.BusinessLayer_Execute_Snapshot` as well as the new sample test. `CompareCommand.DeleteTemporary` now tries 5 times, 500 ms apart, and then leaves the folder to the OS's temp cleanup.
