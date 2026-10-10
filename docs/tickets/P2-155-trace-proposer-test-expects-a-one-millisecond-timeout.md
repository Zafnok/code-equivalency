# P2-155 The trace-proposer test expects rung 4 to give up in one millisecond, and fails when Z3 answers inside it
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`TraceInvariantProposerTests.StateUnpaired_ProvedByTraceProposer` (ticket P1-009) failed once on CI,
on a PR that cannot reach it:

- PR #452 (P2-150, a change to the C# frontend's lowering and its fingerprint), check
  `gates (windows-latest)`, run 38012563196, job 114095652745, head `f43dbfb2`. The same commit
  passed `gates (ubuntu-latest)`. The test took 398 ms.

The assertion that failed is line 219, `Assert.Equal(RungOutcome.Timeout, fromRungFour[0].Outcome)`:
`Expected: Timeout`, `Actual: Proved`. The test gives the ladder `TimeoutMs = 1` so that rung 4
(CHC) gives up on `loops/state-unpaired` and the trace proposer is the one that proves the pair.
On that run Z3 answered rung 4's query inside the millisecond, so rung 4 proved it and the test's
premise did not hold. The verdict was Equivalent either way: this is not a soundness matter and
not a wrong result, only a test whose setup depends on how fast the machine is.

When this is done the test reaches the trace proposer by something a fast machine cannot undo, and
still asserts that the trace proposer, asked first, proves the pair.

## Spec references
`docs/tickets/done/P1-009-trace-mined-invariants.md` (criteria 3 and 5, and the deviation in its
Notes that this test implements), `tests/Equiv.Verify.Z3.Tests/TraceInvariantProposerTests.cs`
(`StateUnpaired_ProvedByTraceProposer`), `src/Equiv.Verify.Z3/` (`LoopLadder`: how a rung is given
its timeout, and whether a rung can be turned off or handed a resource limit).

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` says why rung 4 can answer inside `TimeoutMs = 1` (when the timer starts, what Z3
   does before it first checks it), from reading `LoopLadder` and from at least 200 local runs of
   the test, with the count of each outcome of rung 4.
2. The test no longer depends on a wall-clock race: rung 4 does not prove the pair in it whatever
   the machine's speed (a resource limit, a rung the ladder is told to skip, or a fixture rung 4
   cannot prove), by the smallest of those the ladder already supports. No change under `src/`
   unless none of them exists; then stop and say so in Notes.
3. The snapshot is unchanged, or the pull request says which line changed and why.
4. 200 consecutive local runs of the test pass.

## Files
`tests/Equiv.Verify.Z3.Tests/TraceInvariantProposerTests.cs` and its snapshot.

## Tests
The test itself.

## Size guard
Test code only. If criterion 1 finds that a timeout of 1 ms is not honoured anywhere in the ladder,
that is a finding about the ladder: file it and leave it.

## Out of scope
- The ladder's budgets (ADR 0049).
- Other tests that pass a small `TimeoutMs`; list any that share the race in Notes and file them.

## Notes
- Filed 2026-10-09 from the CI failure on PR #452.
