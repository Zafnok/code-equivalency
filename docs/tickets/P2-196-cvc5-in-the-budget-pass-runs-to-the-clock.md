# P2-196 In the budget pass, cvc5 runs a query to the clock, an hour, without reaching its resource limit
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-033, P1-039

## Goal
ADR 0050 decision 5 gives cvc5 a resource limit (`--rlimit` 2,000,000) so that the limit, not the
clock, ends its queries, and the same query ends the same way on any machine. Its `--tlimit` is the
pass's `timeoutMs`, which P2-077 multiplies for the threads: on 24 threads the budget pass gives a
cvc5 process 3,600 s.

P1-039's thorough runs of `gitextensions-8522` (`docs/runs/2026-10-14-cvc5-partitions.md`) show a
query that never reaches 2,000,000 steps. The `divergence` query of
`GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()` runs plain cvc5 for 370 s in
the first pass and 3,608 s in the budget pass, both ended by the clock, in every run. With
`solvers.cvc5.partitions` set, its split runs another 3,600 s. That one query is most of the 3,500 s
the key adds to the run, and an answer the clock ends can differ between runs and machines. P1-033's
budget measurement already counted 23 of 37 scripts ended by the clock at 60 s.

When done, no cvc5 process in a run of `gitextensions-8522` is ended by the clock, or the ticket
says, from a measurement, which part of cvc5's work its resource count does not see and what bounds
it instead.

## Spec references
ADR 0050 (decision 5), ADR 0049, `docs/runs/2026-10-04-cvc5-budget.md`,
`docs/runs/2026-10-14-cvc5-partitions.md`, `src/Equiv.Verify.Cvc5/Cvc5Solver.cs`,
`src/Equiv.Verify.Z3/SecondSolver.cs`, `src/Equiv.Cli/PairWorkers.cs` (`Sharing`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first: for each cvc5 process the clock ends in a thorough run of `gitextensions-8522`
   with cvc5 configured, where its time goes (parsing, preprocessing, search) and how many resource
   units it had spent. Recorded in `docs/runs/<date>-cvc5-clock.md`.
2. A change chosen from that measurement (cvc5's own resource weights, a limit per pass, or another)
   makes the resource limit end every one of those processes, or the run file says why none can.
3. Two thorough runs of `gitextensions-8522` with cvc5 configured agree on every result cvc5
   answered a query of, and `run.properties.queryEndings` gains, or the run file reports, how many
   cvc5 processes each limit ended.

## Files
`src/Equiv.Verify.Cvc5/**`, `src/Equiv.Verify.Z3/SecondSolver.cs`, tests,
`docs/runs/<date>-cvc5-clock.md`.

## Tests
A unit test of whatever limit or option the change passes to cvc5, with the process stood in for.

## Size guard
A second resource limit for Z3, or a change to Z3's budgets: stop, that is another ticket.

## Out of scope
Splitting (P1-039). The queries cvc5 cannot read (P2-194).

## Notes
- Found by P1-039's thorough runs.
