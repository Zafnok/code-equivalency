# P2-044 A replay driver's protocol stdout is shared with the code under test
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-039

## Goal
P2-039's dumps of the M4-007 Tomas `--execute` hang showed both replay drivers blocked inside a case, in
`Console.WriteLine` called by the method under test (`edu.asu.emit.algorithm.graph.Graph.ImportFromFile(string)`
prints the I/O error it catches). A driver's stdout is also the protocol channel the parent reads outcome lines
from, so anything the code under test prints can be read as a case's answer. That puts the parent and the driver
out of step, and once the pipe fills the two block on each other. P2-039's deadline now ends such a case as
`no answer within 10 s`, so the run finishes. But a method that prints is still never tested, and a printed line
that happens to parse as an outcome would be taken as the method's result.

## Spec references
M3-032 (the driver protocol); ADR 0035 decisions 2 and 3; `src/Equiv.Execute` (the emitted driver and
`ChildProcessHost`); P2-039's Notes (the stacks).

## Acceptance criteria (all must hold; nothing beyond them)
1. Nothing the code under test writes to `Console.Out` or `Console.Error` can reach the parent as a protocol
   line, on either runtime (.NET Framework 4.8 and .NET 10 drivers). How is this ticket's `Decision:`: for example,
   the driver keeps the original stdout for the protocol and points `Console.Out` elsewhere before it runs a case,
   or the protocol moves to its own pipe.
2. A regression test with a target method that prints more than the pipe buffer (over 4 KB) and then returns:
   the case gives that method's real outcome, not a timeout, and the next case still gets the right answer.
3. On the Tomas pair, `Graph::ImportFromFile(string)` no longer ends as
   `notConstructible: ... "no answer within 10 s"`.

## Size guard
Keep the outcome-line format unchanged. Capturing or comparing what the method prints (treating console output
as an observable) is out of scope.

## Out of scope
Sandboxing the code under test (P2-040 covers only the working directory).

## Notes
- Decision (criterion 1): the emitted driver (`DriverSource`'s template, shared by `DriverFactory` and
  `ReplayDriverFactory`) opens its own `StreamWriter` over `Console.OpenStandardOutput()` for the protocol and then
  points `Console.Out` and `Console.Error` at `TextWriter.Null`, before the first case. The protocol pipe is unchanged, so
  `ChildProcessHost` and the outcome-line format are untouched. The writer's default encoding is UTF-8 without a BOM on
  both runtimes, and every protocol line is ASCII anyway. What the code under test prints is dropped, not captured
  (size guard). Code that writes to `Console.OpenStandardOutput()` itself, or calls `Console.SetOut` back to stdout,
  still reaches the pipe; the criterion names `Console.Out` and `Console.Error` only. Likewise `Console.In` is still the
  protocol's stdin, so a method that reads stdin could swallow later case lines; nothing in the corpus does, and P2-039's
  deadline bounds it.
- Criterion 2: `RuntimeDiffTests.WriteLine_PrintsPastThePipeAndForgesAnOutcome_ReturnsItsOwnOutcome` calls the real
  `System.Console::WriteLine(string)` through both runtimes' drivers, first with a 5000-character string, then with the
  string `["Threw","Forged"]`. Every run gets `Returned null` for both cases. Without the fix it fails on all four runs,
  with the first case read as `NotComparable "malformed: aaaa..."` (the printed line taken as the answer).
- Criterion 3: on 2026-09-28 a Release build of this branch ran `equiv compare --execute` on the migrated Tomas pair
  (M4-007's agent copy) on Windows: exit 1 (Divergent present), 103 s, 721 results (EQ001 681, EQ002 2, EQ003 16,
  EQ004 1, EQ005 5, EQ006 16). No result mentions `no answer within`. `Graph::ImportFromFile(string)` is now an
  observed EQ002: on input `"ä\"̈9aH"` the legacy side throws `System.ArgumentException` (.NET Framework
  rejects `"` in a path) and the modern side returns (it catches the I/O error and prints it). A Debug build of the CLI
  stops earlier on this pair, at `Debug.Assert("lowered IR must validate")` in `IrLowerer.cs:186`; that is outside
  this ticket.
