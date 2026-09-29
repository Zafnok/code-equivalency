# P2-043 A Spacer derivation's inputs never give an array a negative length
Status: done (PR #254)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-019

## Goal
M0-012's differential soundness gate failed rule 2 on PR #241 (gates, windows-latest, run 36369761500),
CsCheck seed `6rdKklVqtDVa`, with operator `DropFieldWrite`: "the model gives u the length -1". The
pair loops, so rung 4 (Spacer) decides it. `ChcEncoder.DerivationInputs` decodes the derivation's
inputs itself through `ChcEncoder.Decode`, not `ModelDecoder.Inputs`, so P2-019's clamp never ran.
The encoder's length assumption did reach the CHC segments, since they encode through
`FragmentEncoder`. But the derivation of the dropped `F = x;` never reads `u`'s length, so Spacer
left it free at -1. It is a decoding bug, not a soundness one: the value is never read, so 0 replays
the same way.

## Spec references
VERIFICATION-MODEL.md section 7 (differential soundness); ticket P2-019; ticket P1-001 (rung 4).

## Acceptance criteria (all must hold; nothing beyond them)
1. A Spacer derivation's decoded inputs give every reference a non-negative `length.<Sort>` value.
2. The gate's shrunk pair is Divergent, and its model replays as a divergence in C#.

## Files
`src/Equiv.Verify.Z3/ModelDecoder.cs`, `src/Equiv.Verify.Z3/ChcEncoder.cs`,
`tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`.

## Tests
`ADerivationThatNeverReadsALengthReplaysAsDivergence` (the shrunk pair, rule 2), and the gate at
seed `6rdKklVqtDVa`.

## Notes
- Decision: the clamp moves to `ModelDecoder.Clamped`, which both `ModelDecoder.Inputs` and `ChcEncoder.Decode` call.
  It is not a second assumption in the CHC rules: the rules already assume every length they read is non-negative,
  and a length a derivation leaves free is unread, so 0 cannot change its replay.
- Decision: the regression test is the gate's own pair as a fixed `[Fact]` in `DifferentialSoundnessTests`. A small
  Verify.Z3 pair with a loop and an unread length (tried in `SpacerRungTests`) got length 0 from Spacer, so it would
  pass without the fix. It is not kept.
- P2-019's Notes say "every ladder rung encodes through `ProductEncoder.Encode`". That holds for the assumption, but
  not for decoding: rung 4 has its own decoder.
- Renumbered from P2-041 after merge: #249 (as-array model maps) took P2-041 first, and #246 filed the nullable
  call-identity ticket as P2-041 too (now P2-042). PR #254 and its commit still say P2-041.
