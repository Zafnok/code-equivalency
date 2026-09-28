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
- Decision (criterion 1, from dumps): on 2026-09-28 the old code (`main` 440ab32) was run on Windows against M4-007's
  migrated Tomas pair. It stalled at `execute 311/708 item=edu.asu.emit.algorithm.graph.Graph::ImportFromFile(string)`,
  still on that one item after 408 s. CPU at that point: `Equiv.Cli.exe` 76 s, `EquivReplay4.exe` 0.03 s,
  `dotnet EquivReplay4.dll` 0.09 s. Stacks:
  - Parent (`dotnet-stack report`): `Interop+Kernel32.WriteFile` <- `BufferedFileStreamStrategy.Flush` <-
    `StreamWriter.WriteLine` <- `ChildProcessHost+Session.Exchange` <- `DriverStream.Run` <-
    `DifferentialTester+<>c__DisplayClass8_1.<Test>b__0` <- `Enumerable.ToList` <- `DifferentialTester.Test` <-
    `CompareCommand.Executed`.
  - Modern driver (`dotnet-stack report`): `WindowsConsoleStream.WriteFileNative` <- `StreamWriter.Flush` <-
    `Console.WriteLine(object)` <- `ConsoleUtility.WriteLine(object)` <-
    `edu.asu.emit.algorithm.graph.Graph.ImportFromFile(string)` <- `Program.Case` <- `Program.Main`.
  - Legacy driver (`dotnet-dump collect` works on the .NET Framework process; then `dotnet-dump analyze`,
    `clrstack -all`): `Win32Native.WriteFile` <- `__ConsoleStream.WriteFileNative` <- `StreamWriter.Flush` <-
    `Console.WriteLine(object)` <- `ConsoleUtility.WriteLine(object)` <- `Graph.ImportFromFile(string)`. That call comes
    from the handler for `StreamReader..ctor(string)` -> `FileStream.Init` -> `__Error.WinIOError`: the generated path
    does not exist.

  So the parent blocks where the code review predicted, in `Exchange`'s undeadlined `WriteLine`. The drivers, though,
  are not idle before `Console.ReadLine` as that review guessed. Both are inside a case, blocked writing their own
  stdout: the method under test prints its I/O error with `Console.WriteLine`, and that stdout is also the protocol
  channel. The old `Exchange` reads one stdout line per case, so it can take the printed text as the case's answer
  and move on. Nothing drains the rest of that output, so the driver blocks in `WriteFile`. The parent's next case
  line then fills that driver's stdin, and each side waits on the other's pipe. Putting every wait under one deadline
  and killing the process tree ends this hang as well as the "never reads stdin" one (Tomas run below). The
  underlying cause, the code under test sharing the protocol stdout, is out of this ticket's scope by its size guard
  (no protocol rewrite) and is filed as P2-044.
- Decision: `Exchange` now writes and reads on a pool task under one deadline, and on timeout kills the process tree and
  throws `TimeoutException` (null still means the driver died or passed the memory limit). `DriverStream` turns the
  timeout into `NotComparable` with canonical `"no answer within 10 s"` (`OutcomeLine.TimedOut`), so `runtime-diff` keeps
  its per-case behaviour, while `DifferentialTester` treats it as an obstacle (the pair ends as
  `differentialTesting.notConstructible: the <side> side gave NotComparable "no answer within 10 s"`, both streams
  disposed, so both processes killed) and `Replayer` already made any non-comparable side not constructible. Bound per
  pair: `--test-budget` time + one input (at most 2 cultures x 2 sides x 10 s) + the divergence rerun (2 x 10 s) + the
  replay (2 x 10 s).
- Criterion 4: on 2026-09-28 this branch (7389a1c, a base that predates P2-033's fix #255) ran
  `equiv compare --execute` on the Tomas pair on Windows. It finished in 82 s wall-clock (execute phase 12.9 s,
  708 procedures) and wrote `equiv.sarif` with 720 results: EQ001 681, EQ003 17, EQ006 15, EQ005 5, EQ002 1, EQ004 1.
  Exactly one result names the timeout: `Graph::ImportFromFile(string)`, with
  `differentialTesting.notConstructible: the modern side gave NotComparable "no answer within 10 s"`. No
  `EquivReplay4` process was left running. The exit code was 5 (`InternalError`), from the one pair-level crash,
  P2-033's `IrSortValue ... was not present in the dictionary` on `GetExpectedWeightAndNodes(string)`, which is fixed
  on `main` but not in this branch's base. It is not an execution failure. `DriverDeadlineTests` (both cases) pass on
  Windows, where the pipe buffer is 4 KB.
