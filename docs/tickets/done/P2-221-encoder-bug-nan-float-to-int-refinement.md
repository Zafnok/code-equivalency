# P2-221 Encoder bug: a NaN through `conv.f64.i32` gives a model whose replay does not diverge
Status: done (PR #523)
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
- Where they part ways (criterion 1): per argument. The solver and the replay agree on the side and the application;
  they disagree on what one NaN is. `InterpretedPure.Truncated` gives `conv.f64.i32` on a NaN the shared uninterpreted
  function `pure:conv.f64.i32(sort<System.Double>)->bv32` (`PureEncoder.Apply`'s fallback). A refined query is solved
  after the `fpa2bv` tactic (`Z3Backend.Pipeline`, `floats: true`), which rewrites that function into one over the
  argument's sign, exponent and significand bits. IEEE 754 has one NaN value but 2^53 - 2 NaN bit patterns: the input
  `%v0` is a free constant whose bits the solver picks, and `%v0 - %v1` is the NaN pattern `fpa2bv`'s subtraction makes,
  so the solver read `f(%v0)` and `f(%v0 - %v1)` as two applications on two arguments and answered them differently.
  The model is read back in IEEE terms: `ModelDecoder.Values.Float` decodes any NaN to the one NaN of `IrFloat`, and
  `ModelOracle.Answer(IrPure, ...)` evaluates the function at that one NaN for both sides, so both replays got one
  answer and agreed, and `EnsureDiverges` threw. `SortMapperTests.OneNaNGivesAnUninterpretedFunctionOneNaN` pins the
  Z3 behaviour: after `fpa2bv`, `isNaN(x) and f(x) != f(NaN)` is satisfiable.
- Fix: `SortMapper.OneNaN` maps a floating-point argument to `ite(isNaN(x), NaN, x)`, so every NaN reaches an
  uninterpreted function as the one bit pattern `fpa2bv` gives the NaN literal. `PureEncoder.Apply` applies it to the
  arguments of every uninterpreted pure function (the result and `threw` functions, and the fallback of an
  interpreted conversion), and `TraceEncoder.Call` to a call's arguments. The interpreted terms keep the raw arguments:
  IEEE operations treat every NaN alike. Nothing changes for an argument that is not a NaN, so no verdict on an input
  inside every conversion's meaning moves (criterion 3: `RefinementTests`, `InterpretedPureTests`, `PureEncoderTests`
  and the whole `Equiv.Verify.Z3.Tests` project pass unchanged). Nothing changes what a conversion means on either
  side, so the size guard does not trip.
- The same fix covers call arguments, so it is applied there (Out of scope allows it): a probe pair passing a NaN to
  `Sink::F(double)` on one side and the same NaN narrowed to `float` and widened back on the other threw the same
  "Encoder bug" with the pure-function fix alone; with `TraceEncoder.Call` fixed it is Equivalent
  (`ACallGivenANaNIsGivenOneNaN`). Not covered: a heap map whose values or keys are floating point, passed whole to a
  call's functions, can still hold two NaN patterns that `fpa2bv` tells apart; filed as P2-227.
- The seeded pair is Divergent on in-range inputs (`%v0 = 3, %v1 = 2`), but after the fix the solver's first refined
  model is one where a conversion is outside its meaning, so the verdict is Unknown(abstraction) on `conv.f64.i32`
  with a tainted candidate that does differ. `TheSeededPairReplaysToADifference` accepts either a Divergent that .NET's
  arithmetic reproduces or that Unknown, and fails on the encoder bug, which it threw before the fix.
- `TruncatingANumberMinusZeroIsNeverDivergent` (`conv(a)` against `conv(a - 0.0)`) threw the same encoder bug before
  the fix. After it, the refined query hits the resource limit (proving `a - 0.0` has `a`'s bits through bit-blasted
  subtraction is expensive), so the pair stays Unknown and the test asserts only that it is not Divergent.
- Decision: canonicalise NaN at the uninterpreted application, not at the inputs. An input, a call result, a field read
  and a literal can each carry any NaN pattern; the application is the one place every one reaches.
- Decision: the ticket's "same pair with the NaN replaced by an out-of-range finite double" fixes `%v0` to a constant
  and keeps `%v1` a parameter. On a NaN both sides convert the one NaN and are Equivalent; on `1e300` and
  `-2147483649.0`, `%v0 - %v1` is another out-of-range argument wherever it is not `%v0`, so the pair stays
  Unknown(abstraction) with a candidate that differs. Fixing `%v1` too made the pair constant-fold and pass before the
  fix, so it pinned nothing.
