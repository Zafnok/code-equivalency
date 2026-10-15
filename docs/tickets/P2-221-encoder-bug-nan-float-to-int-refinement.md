# P2-221 Encoder bug: a NaN through `conv.f64.i32` gives a model whose replay does not diverge
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`RefinementSoundnessTests.ARefinedVerdictHoldsOnDotNetArithmetic` fails on `main` (`70ad5fe7`) with CsCheck seed
`9MGZCy1Yn9U3`, on Linux CI and on Windows. `ModelDecoder.EnsureDiverges` throws "Encoder bug": the solver's model makes
the two sides differ, but replaying the model makes them agree. The guard did its job. Nothing was reported Divergent,
but the pair fails instead of getting a verdict, and any CI run that draws a seed like this one goes red.

The pair, as the test printed it:

```
old: %v2n = conv.f64.i32(%v0); %v2 = conv.i32.f64(%v2n); %v3n = conv.f64.i32(%v2); %v3 = conv.i32.f64(%v3n); ...; ret f64.le(%v3, %v1)
new: %v2 = f64.sub(%v0, %v1);  %v3n = conv.f64.i32(%v2); %v3 = conv.i32.f64(%v3n); ...; ret f64.le(%v3, %v1)
```

The inputs are `v0` = NaN (bits `0x7FF8000000000000`) and `v1` = a tiny negative double (bits `0x8800000000000000`). Both
replays return `false`. A NaN converted to `int` is outside the conversion's meaning (M4-002, P1-030), so the model picks
its value. The solver and the replay oracle must read that value the same way, and here they do not: per application,
per side, or per argument (two NaN operands being one SMT value). Finding which one is the ticket.

When done, a model of this pair either replays to a difference or is not produced, and the seed passes.

## Spec references
`src/Equiv.Verify.Z3/ModelDecoder.cs` (`Replay`, `EnsureDiverges`, the oracle), the `conv.f64.i32` encoding and its
refinement (`docs/tickets/done/M4-002-pure-operators.md`, `docs/tickets/done/P1-030-refine-pure-functions-on-a-candidate.md`),
`tests/Equiv.Verify.Z3.Tests/RefinementSoundnessTests.cs`, and the ROADMAP's post-MVP floating-point bullet, which leaves
an out-of-range float-to-int conversion uninterpreted until a pair's side of .NET 9 is known.

## Acceptance criteria (all must hold; nothing beyond them)
1. Notes say where the solver's value for an out-of-meaning conversion and the replay's value part ways, citing the code.
2. `ARefinedVerdictHoldsOnDotNetArithmetic` passes with `CsCheck_Seed=9MGZCy1Yn9U3`, and a unit test pins this pair.
3. No verdict on an input inside every conversion's meaning moves (the existing refinement and pure-operator tests pass
   unchanged).

## Tests
The pinned pair above, and the same pair with the NaN replaced by an out-of-range finite double.

## Size guard
A change to what an out-of-meaning conversion means on one side alone (ADR 0025's runtime-sensitive split): stop and
route it through `equiv-adr`.

## Out of scope
Other operators' out-of-meaning values, unless the same fix covers them.

## Notes
- Filed 2026-10-15 from GH-407's PR: the `sonar` job's test run drew this seed. Reproduced on a clean `main` checkout
  on Windows with the same seed.
