# ADR 0038: A run reports its progress on stderr, with a clock per phase, an ETA, and a writer that never blocks the pipeline

Status: accepted (2026-09-27)

## Context
The M4-007 `full` run on the corpus had been going for 2.5 hours on 2026-09-27 with no way to tell
which phase it was in, which pair it was on, or how much was left. `equiv compare` writes only
errors and the analysed-lines line. Loading, matching, lowering, the per-pair verification loop
(`CompareCommand.Verified`), `--execute` and the SARIF write are silent. One pair can hold the loop
for `TimeoutMs` × rungs × queries, and from outside that looks the same as a hang. The only choice
was to wait or kill the run, and killing it throws the run away.

## Decision
`equiv compare` takes `--verbosity quiet|normal|debug` (default `normal`) and `--log <path>`.
Progress goes to stderr, and also to the `--log` file when one is given. It never goes to stdout,
which stays reserved for results and for `equiv mcp`'s protocol (ADR 0033). SARIF does not change.
`Equiv.Core` owns a small contract, `IRunLog`: `Phase(name, total)`, `Item(identity, weight)`,
`ItemDone(outcome)`, `Detail(text)` (debug only) and `PhaseDone()`, plus `NullRunLog`. The frontend
and the backend receive an `IRunLog` and call it at their natural boundaries: a project loaded, a
procedure lowered, a pair started and finished, and a rung started and finished (debug only).
`Equiv.Cli` owns the one real implementation. Each call is an O(1), allocation-light
`TryWrite` to a bounded `System.Threading.Channels` channel. The call also updates a snapshot of
the current phase and item through `Interlocked`, and never waits. One background task drains the
channel and writes lines. On a `PeriodicTimer` it also prints a heartbeat from the snapshot, so a
pair stuck in Z3 is still reported by name and by elapsed time. Every duration comes from a
`TimeProvider` timestamp (Stopwatch-backed in production, fake in tests). The ETA is a pure
function in Core: remaining weight × (elapsed / completed weight), with the worst-case bound
(remaining solver pairs × `TimeoutMs` × rung count) printed beside it. At the end of each phase the
log prints the ETA it gave at 25%, 50% and 75% next to the actual time, so the heuristic can be
checked against real runs.

## Why
- The loss is operational, not a precision problem: a multi-hour corpus run could not be told
  apart from a hang, and M4-007's remaining runs (`seeded`, `--execute`) are just as long.
- A heartbeat driven from the writer's own timer is the only design that reports a pair that never
  returns. Per-item logging from the worker says nothing while the worker is stuck in `Solver.Check`.
- `TryWrite` on a bounded channel keeps the verify loop's cost at one interlocked write plus one
  enqueue per event. If the writer falls behind, the log drops events and counts the drops. It
  never slows the pipeline down.
- Weighting by IR size (instructions on both sides, times a loop factor for pairs that reach the
  loop ladder) follows where solver time actually goes. Congruent and unbound pairs never reach the
  solver and get a near-zero weight, which is what a plain item count gets wrong.
- Printing the ETA's error at the end of each phase turns the heuristic into something measured,
  in the spirit of ADR 0027.

## Rejected
- `Microsoft.Extensions.Logging`: it adds a dependency category (ADR 0002) for one sink and a level
  switch. Its providers also format on the calling thread.
- Progress in SARIF (`invocations`, run properties): SARIF is the result. Timings would make
  snapshot tests nondeterministic and say nothing about a run that is still going.
- Writing straight to `Console.Error` from the worker: every write takes the console lock and does
  I/O on the verify thread, and it goes quiet during a long solver call.
- An ETA from item count alone: congruent pairs finish in microseconds and loop pairs in seconds,
  so the estimate swings by orders of magnitude as the mix changes.
- A per-pair wall-clock cap: that is a verdict-affecting change (a new Unknown reason), not logging.
  It is out of scope here and needs its own ticket if the logs show it is needed.

## Consequences
- This is a Core contract change. `ILanguageFrontend.Analyze` gains an `IRunLog` parameter just
  before its `CancellationToken`. The backend gets its log through `VerificationOptions.Log`, an
  `init` property that defaults to `NullRunLog.Instance`, so `IVerificationBackend.Verify` and its
  roughly 30 callers and test doubles keep their signature.
- ARCHITECTURE.md's extension-point table gains an `IRunLog` row (CLI channel writer; an MCP progress
  notification writer is planned for M5).
- The corpus skill passes `--verbosity debug --log "$run/progress.log"` on every run, gains a way to
  read progress (`corpus.ps1 -Progress <run>`), and SUMMARY.md gains a per-phase time table.
- Tickets: M4-012 (contract, writer, ETA, CLI phases), M4-013 (frontend events), M4-014 (backend
  events), M4-015 (corpus skill and script). None blocks M4-007, and the run in progress is not
  affected.
