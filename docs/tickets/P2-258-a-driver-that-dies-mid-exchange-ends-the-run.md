# P2-258 Crash: a replay driver that dies while its exit is still being torn down ends the whole run with exit 5
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-247's `full --execute` run of `gitextensions-8522` at `4b2e0df6` (`main`), with `Microsoft.WindowsDesktop.App` 5.0.17
present, exited 5 after 386 s and wrote no SARIF. The `execute` phase replayed
`GitExtensions.Plugins.GitStatistics.PieChart.PieChart3D::GetSliceDisplacement(float,float)`, which no earlier run could
build ("runtime net5.0-windows not installed", P2-250's run): both sides' drivers stopped on the member's own
`Debug.Assert` ("Process terminated. Assertion failed."), each took about 18 s to go, and then equiv printed "Process
has exited, so the requested information is not available." and ended. That is `InvalidOperationException` from
`ChildProcessHost.Session.Exchange` (`src/Equiv.Execute/ChildProcessHost.cs`): it tests `process.HasExited` and then
reads `process.PrivateMemorySize64`, and a process that fail-fasts is gone from the process snapshot before its handle
signals exit, so the second read throws. Nothing catches it, so one member's driver ends a run of 13,592 pairs. When
done, a driver that dies at any point of an exchange is a dead driver (the outcome `Exchange` already gives a driver
that died, `null`), never an exception out of the run, and one pair's execution failure never costs the run its SARIF.

## Spec references
ADR 0035 decision 2, ADR 0062, ticket P2-039 (the exchange deadline), `src/Equiv.Execute/ChildProcessHost.cs`
(`Session.Exchange`, `Kill`), `Replayer`, `docs/runs/2026-10-20-precision-execute-verdict-2.md` (the crashed run).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test of `ChildProcessHost` whose driver exits while `Exchange` polls it (between the exit test and the memory
   read, made deterministic by a seam or a fake process view) returns the dead-driver outcome and throws nothing.
2. An integration test against a `samples/` pair whose Divergent member fails a `Debug.Assert` (or calls
   `Environment.FailFast`) on both sides under `--execute` writes SARIF, exits 1, and gives that member's replay a
   `not-constructible` reason that says a driver died.
3. `gitextensions-8522` in `full --execute` with `Microsoft.WindowsDesktop.App` 5.0 present (P2-253's private root, as
   P2-247 made it) completes, and `GetSliceDisplacement(float,float)`'s replay value is given in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (criterion 2: an exit 5 names its procedure and gets its crash ticket).
- The run's `console.txt` and `progress.log` are under P2-247's worktree, `.corpus/pairs/gitextensions-8522/runs/20261020-1410-full-quick-execute`.
- P2-247 reran the pair without `Microsoft.WindowsDesktop.App` 5.0 (its criterion 1's "execution unavailable"), so
  its desktop members are not constructible there and the verdict rests on that run.
