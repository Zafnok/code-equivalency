# P2-131 The backend counts how every query ended, and the run reports that count
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-077

## Goal
`run.properties.queryEndings` (P2-077) says how many queries the resource limit ended and how many the
wall-clock backstop ended. P2-077 could not change `Equiv.Verify.Z3` beyond thread safety, so
`Equiv.Cli` counts from the text of the ladder steps in the run's results: a timed-out rung whose
detail says `resource limit` or `wall-clock limit`. That misses every query that is not the one that
timed a reported rung out: the reachable-opaque queries, ADR 0037's failure-refinement queries, the
contract search, a contract attempt that proved nothing, and a query a pair made before a later rung
decided it by another route. It also ties the CLI to the wording of a detail. Count in the backend,
where every check goes through `Z3Backend.Check` and every fixedpoint query through `ChcEncoder`,
and report the count through a contract in `Equiv.Core`.

## Spec references
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Check`, `LimitHit`), `src/Equiv.Verify.Z3/ChcEncoder.cs`,
`src/Equiv.Verify.Z3/Stages.cs`, `src/Equiv.Cli/QueryEndings.cs`, VERIFICATION-MODEL.md section 6
(solver budgets), ticket P2-077 criterion 4.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every solver check and fixedpoint query the backend makes is counted once by how it ended:
   answered, ended by the resource limit, ended by the wall-clock backstop, interrupted (P2-076), or
   given up for another reason. The count is safe to add to from several threads.
2. `run.properties.queryEndings` is filled from that count and keeps its two keys, `resourceLimit`
   and `wallClock`, with the same meaning; the other endings are new keys beside them.
   `QueryEndings` no longer reads a ladder step's detail.
3. A second solver's answers (ADR 0050) are counted under a key of their own.
4. No verdict, rule id, fingerprint or ladder trace changes.

## Files
`src/Equiv.Core/VerificationOptions.cs` or a new type beside it, `src/Equiv.Verify.Z3/Z3Backend.cs`,
`src/Equiv.Verify.Z3/ChcEncoder.cs`, `src/Equiv.Verify.Z3/SecondSolver.cs`, `src/Equiv.Cli/QueryEndings.cs`,
`src/Equiv.Cli/CompareCommand.cs`, `docs/VERIFICATION-MODEL.md`, their tests and snapshots.

## Tests
`Z3BackendTests.EveryCheckIsCountedByHowItEnded`, `ChcEncoderTests.EveryFixedpointQueryIsCounted`,
`CompareCommandTests.QueryEndingsComeFromTheBackend`.

## Size guard
A change to which queries are made, or to any budget, means the ticket has been misread: stop.

## Out of scope
Per-pair counts in a result's properties. Timing of queries (the stage lines of P2-076 have it).

## Notes
- Found 2026-10-04 by P2-077, whose size guard made this a ticket.
