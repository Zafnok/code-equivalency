# P2-039 `compare --execute` hangs forever when a replay driver never answers
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-009, P1-008

## Goal
M4-007's `--execute` run on `pmb-tomasjohansson__adapters-shortest-paths-dotnet` stopped making
progress about a minute after it started and never finished. After 2h50m the run's
`Equiv.Cli.exe` held 65 CPU-seconds and its two replay drivers (`EquivReplay4.exe` on .NET
Framework 4.8 and `dotnet EquivReplay4.dll` on .NET 10, both emitted from the project
`Programmerare.ShortestPaths.Adaptee.YanQi`) held 0.03 CPU-seconds each, with no change over a 20s
sample. Nothing was written past `analysed lines of code`, and the run wrote no `exit.txt`. The
documented limits did not stop it: `docs/VERIFICATION-MODEL.md` says differential testing stops at
"`--test-budget` (default 10,000 inputs or 60 s per pair)" and each case has "M3-032's per-case
timeout". Whatever is waiting is waiting without a deadline: a driver blocked before it reads its
first case, a parent blocked reading a driver's stdout, or the two blocked on each other's pipe.

The same run's earlier, non-`--execute` pass on this pair completes in about a minute, and the one
pair-level crash it hits is P2-033, so the hang is in the execution path. It is deterministic
enough to have hit the first pair that needed a driver from that project; reproduce with:

```
equiv compare --legacy <tomas legacy sln> --modern <tomas modern sln> --execute --out equiv.sarif
```

(`tools/corpus/corpus.ps1 -Fetch` and `-PrepareAgent` for `TomasJohansson/adapters-shortest-paths-dotnet`
rebuild the inputs; the migration is the agent's output, so any .NET Framework to .NET 10 pair whose
library also multi-targets `net20`/`net40` before migration may do.)

## Spec references
ADR 0035 decisions 2 and 3; `docs/VERIFICATION-MODEL.md` replay and differential-testing sections;
M3-032 (the driver protocol and per-case timeout); `src/Equiv.Execute`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Find where the parent and the two drivers block, with a dump of each process (`dotnet-stack` or
   `dotnet-dump`) rather than a guess, and record it as a `Decision:`.
2. Every wait on a driver has a deadline: a driver that never answers ends its pair as
   `not-constructible` (or Unknown with a `differentialTesting.notConstructible` reason naming the
   timeout), kills both driver processes, and the run continues. The overall run can no longer
   outlive `pairs x (--test-budget + a fixed grace)`.
3. A regression test with a driver stub that never reads stdin, and one that never writes stdout.
4. The Tomas `--execute` run completes.

## Size guard
If the root cause is the emitted driver for a multi-targeted project rather than the protocol
(for example the .NET Framework driver picks the wrong target), fix the emitter and keep criterion 2
as the safety net; do not widen this ticket into a rewrite of the driver protocol.

## Out of scope
Making replay or testing succeed on the pair; only stopping it from hanging.

## Notes
- Decision (criterion 1, from code, not a dump): this session ran in a Linux cloud container with no .NET Framework
  4.8, no corpus checkout and no Windows process to attach `dotnet-stack`/`dotnet-dump` to, so the dumps the criterion asks
  for were not taken. The one wait in `src/Equiv.Execute` with no deadline was `ChildProcessHost.Session.Exchange`'s
  synchronous `StandardInput.WriteLine` + `Flush`: the case clock started only after it returned. A driver that has not
  read stdin (the M4-007 drivers held 0.03 CPU-seconds each, too little for either runtime to have reached its read loop)
  lets a case line longer than the pipe buffer (4 KB for .NET's anonymous pipes on Windows; the generators' strings and
  arrays pass that) block `WriteFile` for good. That fits every observation: the tester's legacy driver answered and sat
  in its read loop, the modern driver was started and never read, and the parent's CPU stayed flat (the read wait polls
  every 20 ms, so a parent waiting there would have kept ticking). Proved on Linux, where the pipe buffer is 64 KB: with
  the old `Exchange`, `DriverDeadlineTests.ADriverThatNeverReadsStdin_TimesOutAndIsKilled` (1 MB line) hangs until the
  test timeout; with the new one it passes. A Windows dump of the Tomas run is still owed to confirm this is the M4-007
  hang and not a second one.
- Decision: `Exchange` now writes and reads on a pool task under one deadline, and on timeout kills the process tree and
  throws `TimeoutException` (null still means the driver died or passed the memory limit). `DriverStream` turns the
  timeout into `NotComparable` with canonical `"no answer within 10 s"` (`OutcomeLine.TimedOut`), so `runtime-diff` keeps
  its per-case behaviour, while `DifferentialTester` treats it as an obstacle (the pair ends as
  `differentialTesting.notConstructible: the <side> side gave NotComparable "no answer within 10 s"`, both streams
  disposed, so both processes killed) and `Replayer` already made any non-comparable side not constructible. Bound per
  pair: `--test-budget` time + one input (at most 2 cultures x 2 sides x 10 s) + the divergence rerun (2 x 10 s) + the
  replay (2 x 10 s).
- Criterion 4 (the Tomas `--execute` run completes) was not run here, for the same reason as criterion 1: it needs the
  Windows corpus box (`tools/corpus/corpus.ps1 -Fetch -PrepareAgent`, then `equiv compare --execute`).
