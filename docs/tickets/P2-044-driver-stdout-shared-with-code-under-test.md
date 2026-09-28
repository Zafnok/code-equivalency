# P2-044 A replay driver's protocol stdout is shared with the code under test
Status: todo
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
