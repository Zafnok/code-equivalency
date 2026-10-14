# P1-042 Rungs 2 and 3 compare the call traces by position, as rung 1 does
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-038

## Goal
P1-038 made rung 1 compare the two call traces by position, without sequences. That way Z3
answers 106 of the 210 rung 1 queries it gave up on. Rungs 2 and 3 still compare traces as
sequences, and so do the cut events of a loop segment. P1-026 found the cost of that for proof
certificates (`docs/runs/2026-10-14-proof-certificate-spike.md`): cvc5 proves all 37 rung 2
obligations of `gitextensions-9860`'s lockstep Equivalents unsatisfiable that it can read. Yet 35
of those proofs cannot be written in Alethe ("contains operator seq.unit"), so none of the 17
`lockstep-induction` Equivalents can be certified. 12 are blocked by that alone.

When done, the products of rungs 2 and 3 compare call traces by position wherever rung 1's would.
No result a full run decides today becomes a timeout.

## Spec references
`docs/tickets/done/P1-038-*.md` (the form, its cap, and the at-most-once-per-path argument, which
it says a loop segment's cut events need too), `src/Equiv.Verify.Z3/PositionalTrace.cs`,
`LockstepInduction.cs`, `KInduction.cs`, `IrFragmenter.Segment`, VERIFICATION-MODEL.md section 5.1,
ADR 0057.

## Acceptance criteria (all must hold; nothing beyond them)
1. The products `LoopLadder.Check` builds for rungs 2 and 3 compare call traces by position, cut
   events included. P1-038's cap applies, and above it a product keeps the sequence form.
   Wherever the comparison is built, a comment states why each site is reached at most once per
   path in a segment.
2. A property test: on generated pairs with calls inside loops, rungs 2 and 3 give the same
   verdict in both forms whenever both answer.
3. Full runs of `gitextensions-9860` and `gitextensions-8522` in thorough mode, before and after:
   no result decided before becomes a timeout. The counts that changed go in `docs/runs/`.
4. P1-026's spike, rerun on `gitextensions-9860`, reports how many `lockstep-induction` results now
   get an Alethe proof of every obligation, and how many of those Carcara certifies.

## Files
`src/Equiv.Verify.Z3/LoopLadder.cs`, `LockstepInduction.cs`, `KInduction.cs`,
`PositionalTrace.cs`, `docs/VERIFICATION-MODEL.md`, `docs/runs/<date>-positional-induction.md`,
tests.

## Tests
The property test of criterion 2. A unit test per rung on a fixture whose loop calls a shared
function, asserting the printed obligation holds no `Seq` sort.

## Size guard
A change to how rung 1 compares traces, to the cap, or to what a cut event holds beyond its trace:
stop.

## Out of scope
Building certificates (P1-041). Rungs 4 and 5.

## Notes
- Found by P1-026.
