# P2-059 Rung 4 proves a pair only when Spacer's invariant solves the clauses
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-001

## Goal
Rung 4 is unsound on at least one pair. `LadderPropertyTests.NoRungProvesATerminatingCallFreeMutant`
fails with `CsCheck_Seed=4FfExD8adOs4`: Bounded and KInduction refute a mutant (a swapped `shl`
after a loop that never runs), and rung 4 proves it over the bitvectors with the invariant
`old B1 ~ new B1: (= ((_ extract 7 0) old.b.7) ((_ extract 7 0) new.b.7))`. The clauses are right:
the entry rule, the B1~B1 to exit~exit step and the exit~exit divergence rule derive `bad`. Z3 5.1's
Spacer answers unsatisfiable anyway, and its answer defines `inv.exit.exit` as false, which the
B1~B1 to exit~exit rule breaks: `ChcEncoder.Solves` rejects it. With `xform.inline_eager` or
`xform.inline_linear` on, Spacer answers satisfiable; rung 4 turns inlining off on purpose
(`ChcEncoder.Query`, for `DerivationInputs`). So a Spacer answer is not a certificate on its own.
Check every unsatisfiable answer against the clauses it answers before calling it a proof.

## Spec references
VERIFICATION-MODEL.md section 5.1 (rung 4), ADR 0008 (loop ladder), `src/Equiv.Verify.Z3/SpacerRung.cs`,
`src/Equiv.Verify.Z3/ChcEncoder.cs` `Solves`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Rung 4 is Equivalent only when the answer it reports solves the clauses of the arithmetic it
   names: the wrapping clauses for an integer answer read with wrap-around arithmetic (as today),
   the integer clauses for an integer answer that stands on the overflow query, and the bitvector
   clauses for a bitvector answer (`ChcEncoder.Solves` on the encoder that asked).
2. An unsatisfiable answer that does not solve its clauses is Unknown(`ChcSpurious`), Inconclusive,
   and the detail says Spacer's invariant does not solve the clauses. No new reason, no new rule id.
   In integer mode it falls through to the overflow and bitvector queries, as a spurious derivation does.
3. The pair of seed `4FfExD8adOs4` is a fixture, and rung 4 on its own is not Equivalent on it.
4. `NoRungProvesATerminatingCallFreeMutant` passes with `CsCheck_Seed=4FfExD8adOs4`, pinned as its own test.
5. VERIFICATION-MODEL.md section 5.1 and ADR 0008's Clarifications say that `ChcSpurious` also covers
   an answer that does not check.

## Files
`src/Equiv.Verify.Z3/SpacerRung.cs`, `src/Equiv.Verify.Z3/ChcEncoder.cs` (doc of `Solves`),
`src/Equiv.Core/Verdicts/UnknownReason.cs` (doc), `docs/VERIFICATION-MODEL.md`,
`docs/adr/0008-loop-ladder.md`, `tests/Equiv.Verify.Z3.Tests/Fixtures/loops/chc-uncertified.ir`, tests.

## Tests
`SpacerRungTests.AnAnswerThatDoesNotSolveTheClausesIsSpurious`,
`LadderPropertyTests.NoRungProvesTheMutantOfSeed4FfExD8adOs4`,
`LadderFixtureTests` (the new fixture listed).

## Size guard
No change to the clauses, the Spacer parameters or the replay. Turning inlining back on is a
change to what `DerivationInputs` relies on: stop.

## Out of scope
Reporting the Spacer bug upstream, and retrying with other Spacer parameters when an answer fails
its check.

## Notes
- Diagnosis: dumping the bitvector divergence clauses of the pair showed them correct; Spacer's answer set
  `inv.exit.exit`, `inv.B1.exit` and `inv.exit.B1` to false and `inv.B1.B1` to the low byte of `b` being equal, and
  `ChcEncoder.Solves` on that answer is false. `xform.inline_eager=true` or `xform.inline_linear=true` alone makes
  Spacer answer satisfiable; `spacer.global`, `spacer.ground_pobs` and `xform.slice` do not matter. The loop's guard
  (`ult %i, 0`) is always false, the case `Query`'s doc says inlining is off for.
- Decision: an answer that fails its check reuses `ChcSpurious` (an ADR 0008 clarification, not a new ADR): the reason
  already means "Spacer's answer is an artefact", and a new reason would change the SARIF surface.
- Decision: the integer answer that also solves the wrapping clauses is not checked again against the integer clauses;
  the wrapping check is the certificate for the bitvector claim it makes.
- Decision: the pinned seed runs through the property's own generator (`iter: 1`, `seed:`); at the property's 500 ms
  budget Spacer can time out under load, so the stable regression is `SpacerRungTests` on the fixture at 10 s.
