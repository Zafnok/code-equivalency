# P1-017 M0-012's differential soundness gate also verifies every generated pair through the IL lowering
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016

## Goal
M0-012's three rules hold for the IL lowering too. Each generated pair is verified a second time,
with both sides forced through `IlLowerer`, and the IL verdict is held to the same rules as the
IOperation verdict. A false Equivalent that enters only through the IL lowering is caught before
P1-018 can turn the fallback on. About 2 source files and 2 test files.

## Spec references
ADR 0039 (the IL mode); VERIFICATION-MODEL.md section 7 (differential soundness); M0-012's ticket.

## Acceptance criteria (all must hold; nothing beyond them)
1. `DifferentialSoundnessTests` verifies each pair twice, once per lowering. Both verdicts are held
   to rules 1 to 3, and a failure names the lowering that broke the rule.
2. The PR budget stays 200 pairs, and the nightly budget stays 5,000. The IL pass on the PR budget
   adds at most 50% to the test's wall-clock time, measured on CI and stated in the PR.
3. `PairGen` gains at least the constructs the IL lowering exists for: a lifted `int?` operator, a
   nullable conversion, `$"..."` interpolation, and a positional pattern in a `switch`. Each appears
   in the preserving and the changing families.
4. A deliberately broken IL mapping (a test-only `IlLowerer` option that swaps `sdiv` for `udiv`)
   makes the gate fail rule 1 within the PR budget, with the seed printed.
5. No rule is weakened or skipped for the IL lowering.

## Files
- `tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`, `PairGen.cs`
- `src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs` (the test-only option, `internal`)
- `tests/Equiv.Tests.Integration/PairGenTests.cs`

## Tests
- `DifferentialSoundnessTests.GeneratedPairsAreSoundUnderBothLowerings`
- `DifferentialSoundnessTests.ABrokenIlMappingIsCaught`
- `PairGenTests.GeneratesTheIlFallbackConstructs`

## Size guard
More than 4 files, or a change to the rules' text, means you are rewriting M0-012.

## Out of scope
Fixing an IL lowering bug the gate finds: file it as a P2 ticket and note it here, unless it is a
one-line mapping fix.

## Notes
