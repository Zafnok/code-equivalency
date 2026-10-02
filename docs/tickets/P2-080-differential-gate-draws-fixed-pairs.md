# P2-080 The differential gate draws the same pairs on every pull request, as M0-012 says it does
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-017

## Goal
M0-012's criterion 3 gives the gate a fixed seed, and its Notes say the three rule tests "draw the same
pairs because they share the seed". They do not. CsCheck's `Sample(..., seed:, iter:)` uses the seed
for the first iteration only and draws every other iteration at random (CsCheck 4.9.1's XML doc:
"The initial seed to use for the first iteration"). So `DifferentialSoundnessTests` and
`ContractSoundnessTests` check one fixed pair and 199 (or 39) random ones on each run. Three local
runs of `ABrokenIlMappingIsCaught` reported three different seeds. Two things follow. A pull request
can go red on a pair it did not touch, and a test that expects a catch within the budget can miss:
`ABrokenIlMappingIsCaught` failed on `main` at 9f85873 and on PR #319, which changes only docs. That
test now pins a seed of its own. This ticket makes the pull-request budget deterministic, so a red
gate on a pull request is caused by the pull request. The nightly run stays random, which is where
new pairs are meant to be found.

## Spec references
`docs/tickets/done/M0-012-*.md` criterion 3 and Notes, `docs/tickets/done/P1-017-differential-gate-il-mode.md`,
`tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs` (`Sample`), `ContractSoundnessTests.cs`,
`CongruenceSoundnessTests.cs` (which already samples one array with `iter: 1`), `docs/QUALITY-GATES.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. With the pull-request budget, `DifferentialSoundnessTests` and `ContractSoundnessTests` draw the same
   cases on every run at one seed. A test draws the budget twice and asserts the two lists are equal.
2. A failure still prints what replays it: the seed, and the index of the failing case if the cases
   are drawn as one array.
3. `EQUIV_DIFFERENTIAL_SEED` still overrides the seed, and the nightly budget still draws at random.
4. `ABrokenIlMappingIsCaught` still passes. If it no longer needs its own seed, remove `BrokenIlSeed`.
5. `docs/QUALITY-GATES.md` says in one sentence which budget is deterministic and which is random.

## Files
`tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`, `ContractSoundnessTests.cs`, `docs/QUALITY-GATES.md`.

## Tests
`DifferentialSoundnessTests.ThePullRequestBudgetDrawsTheSamePairs`, `ContractSoundnessTests.ThePullRequestBudgetDrawsTheSameTriples`.

## Size guard
No change to `PairGen`, to the rules, or to anything under `src/`.

## Out of scope
The nightly job's schedule or budget. Shrinking quality.

## Notes
- Found 2026-10-01 while fixing the red `gates (windows-latest)` check on PR #319.
