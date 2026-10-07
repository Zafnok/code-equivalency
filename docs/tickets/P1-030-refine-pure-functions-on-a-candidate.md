# P1-030 Abstraction refinement, part 1: a pure function a candidate depends on is given its real meaning
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-019, M3-016, M4-002

## Goal
ADR 0025 makes every floating-point, `decimal` and user-defined operator a pure function both sides
share, with no meaning. ADR 0026 then turns any divergence that depends on one into
Unknown(abstraction): 246 results on the three large runs. ARDiff (Badihi et al., FSE 2020; citation
from memory, check before relying on it) starts from such an abstraction and refines only what a
spurious counterexample implicates.

P1-019 measured the cheapest refinement, `IntPtr ==` and `!=` as sort equality: 7 of 726 Unknowns on
Git Extensions, six of them real divergences the abstraction was hiding. It counted, and did not
encode, the floating-point kinds. On `gitextensions-9860` those are the largest interpretable group
among the abstractions: `conv.f32.f64` 12, `f64.mul` 10, `f64.div` 10, `f32.sub` 6, `f32.add` 5,
`f32.mul` 4, `conv.f64.i32` 4. It was left unscheduled under the 5% bar; it is scheduled now
(2026-10-03) because a hidden real divergence is worth more than its count.

When done, a pair whose candidate counterexample depends only on interpretable pure functions is
asked again with those functions interpreted, and ends Equivalent, Divergent or Unknown for another
reason. A pair whose candidate depends on anything else is unchanged.

## Spec references
ADR 0025 (the catalogue, runtime-sensitive functions), ADR 0026 (taint, `Abstraction`), ADR 0040
(runtime interval, x87), VERIFICATION-MODEL.md sections 2, 3 (the operator paragraph) and 6,
`docs/runs/2026-09-30-abstraction-spike.md`, `tools/spikes/abstraction-refinement/` (the re-query
this ticket moves into `src/`), `src/Equiv.Verify.Z3/PureEncoder.cs`.

## Design
Interpretable kinds, and nothing else:
- `op:` equality and inequality of `IntPtr` and `UIntPtr`: the IR's sort `eq` and `ne`, never
  throwing (P1-019's decision).
- `f32.<op>` and `f64.<op>` for add, sub, mul, div, neg and the six comparisons, and `conv.f32.f64`
  and `conv.f64.f32`: Z3's floating-point theory, round to nearest even. A value of sort `float` or
  `double` is then a floating-point term in the refined query, inputs included.
- `conv.<int>.<float>` and unchecked `conv.<float>.<int>` only where no runtime rule applies to the
  pair (ADR 0040: saturation across .NET 9).

Never interpreted: any function that is runtime-sensitive for the pair (x87, a crossed row), `%` on
floating point (`fp.rem` is not C#'s `%`), every `dec.*`, any other `op:`, `delegate:` and `get:`.

Refinement. When rung 1 ends Unknown(abstraction), take the candidate's `abstractions`. If every
entry is an interpretable `IrPure`, encode the product again with exactly those function names
interpreted and ask rung 1's queries again. Unsatisfiable is Equivalent. A model is replayed in
`IrInterpreter`, which evaluates the same functions concretely and does not taint their results; an
untainted divergence is Divergent. A model that depends on a further abstraction refines again if
that one is interpretable too, at most three rounds. Anything else keeps the Unknown it had.

Pitfalls.
- The refined encoding must be exact or the result is a false Equivalent. `NaN` comparisons,
  signed zero, and `float` to `double` widening are where it goes wrong; the differential gate is
  what holds this (criterion 5).
- `IrInterpreter` and the encoder must agree bit for bit on every interpreted function. Evaluate in
  the interpreter with .NET's own `float` and `double` arithmetic and check it against Z3 in a
  property test.
- A floating-point input that both sides share is one term; do not give each side its own.
- Floating-point queries are slow. Each refined query has the pair's budgets, and a timeout keeps
  the Unknown(abstraction) result, not Unknown(timeout).

## Acceptance criteria (all must hold; nothing beyond them)
1. A new ADR is merged before any code. It decides that an abstraction is refined on demand, names
   the interpretable kinds, states what makes the floating-point interpretation exact and where it is
   refused, and says how ADR 0025's shared-function rule and ADR 0026's taint rule read for an
   interpreted function.
2. A result decided after refinement has `proofMethod` suffixed `+refined` and carries
   `properties.refined`, the function names interpreted, sorted.
3. New sample `float-arithmetic`: `a * 2.0` against `a + a` is Equivalent, `a + b` against `a - b`
   is Divergent with a model that replays, and `(a + b) + c` against `a + (b + c)` is Divergent. A
   pair whose modern side calls a changed method on the result stays Unknown(abstraction) naming
   that call.
4. The six Git Extensions pairs of P1-019's report (`GitUI/Theming/LocalHook.cs`) are Divergent.
5. M0-012's `PairGen` generates `float` and `double` parameters, the interpreted operators and
   comparisons, with `NaN`, both zeros, infinities and subnormals among its edge values, and the
   three rules hold at the nightly budget before this ticket's PR merges.
6. On a corpus run of `gitextensions-9860` through `equiv-corpus-run`, Notes records how many of the
   52 `abstraction` Unknowns are decided, by outcome, and that no pair that was Equivalent or
   Divergent changed.
7. VERIFICATION-MODEL.md sections 2, 3, 5 and 6 are updated; ROADMAP's post-MVP "Floating point as
   IEEE sorts" line says what remains.

## Files
`src/Equiv.Verify.Z3/PureEncoder.cs`, `SortMapper.cs`, `Z3Backend.cs`, a new
`src/Equiv.Verify.Z3/Refinement/`, `src/Equiv.Core/` (`IrInterpreter`, `ProofMethod`,
`SarifReportWriter`), their tests, `tests/Equiv.Tests.Integration/` (`PairGen`),
`samples/float-arithmetic/**`, `docs/adr/NNNN-*.md` with its README row, `docs/VERIFICATION-MODEL.md`,
`docs/ROADMAP.md`.

## Tests
`Refinement_RunsOnlyWhenEveryAbstractionIsInterpretable`, `Refinement_NeverInterpretsARuntimeSensitiveFunction`,
`Refined_UnsatisfiableIsEquivalent`, `Refined_ModelReplaysUntainted`, `Refinement_StopsAfterThreeRounds`,
`Refinement_TimeoutKeepsTheAbstractionUnknown`, `Interpreter_AgreesWithZ3OnEveryInterpretedFunction`
(property), `NaNComparisons_AreAllFalseButNotEqual`, the sample snapshot, and section 7's soundness
harness run with refinement on.

## Size guard
`decimal`, strings, a user-defined operator's body, or a function kind not in the Design list: stop
and file a ticket with the count from criterion 6's run.

## Out of scope
`string ==` and `!=` (8 results on `gitextensions-9860`; needs a decision on string identity).
`opaque:` fragments, which 195 of P1-019's 238 results hold: IR coverage owns those. Floating point
interpreted from the start for every pair. The reverse direction, abstracting more to beat a timeout
(P1-031).

## Notes
- From the 2026-10-03 improvement review ("Abstraction refinement for hard arithmetic"), scheduled
  by the user the same day against P1-019's "not scheduled".
