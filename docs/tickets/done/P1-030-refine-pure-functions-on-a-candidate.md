# P1-030 Abstraction refinement, part 1: a pure function a candidate depends on is given its real meaning
Status: done (PR #420)
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
- `conv.<int>.<float>` from an integer of at most 32 bits, and unchecked `conv.<float>.<int>` only
  where no runtime rule applies to the pair (ADR 0040: saturation across .NET 9) and only on an
  argument whose truncated value the target type holds (see Notes, the two deviations on
  conversions).

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
- Floating-point queries are slow. Each refined query has the pair's timeout and ten times the
  pair's resource limit (see Notes, the deviation on budgets), and a timeout keeps the
  Unknown(abstraction) result, not Unknown(timeout).

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
- ADR 0053 is its own pull request (#417), as `equiv-adr` says and as ADRs 0044, 0046 and 0048 were, and this
  ticket's branch is stacked on it. It was amended once before merging, from what building this
  measured (the resource limit and the solver pipeline below).
- Deviation: a refined query does not have "the pair's budgets". It has the pair's timeout and ten
  times its resource limit. Measured: under the default limit of 2,000,000 the refined query for
  criterion 3's `a * 2.0` against `a + a` gives up, with `smt`'s own floating-point theory (it needs
  31 million, 4.3 s) and bit-blasted too (3.2 million, 0.33 s). `a * 0.5` against `a / 2.0` needs
  6.8 million bit-blasted and 90 million otherwise. The limit was calibrated on bit-vector queries
  (P2-050). ADR 0053 decision 1 says so.
- Decision: the solver of a refined query -> `solve-eqs, simplify, propagate-values, solve-eqs,
  fpa2bv, simplify, bit-blast, smt`. Alternatives: the unrefined pipeline (ten times the resources on
  the four queries measured); `qffp` (does not read the product's functions and arrays). Rule: 3.
- Deviation: `conv.<float>.<int>` is interpreted only where the truncated value fits the target. A
  pair that does not cross .NET 9 can sit on either side of it, the two sides of .NET 9 disagree out
  of range (saturation against a platform's value), and the backend does not know which side it is
  on. Out of range the function stays shared and tainted.
- Deviation: `conv.<int>.<float>` is interpreted only from an integer of at most 32 bits. A double
  holds every such integer, so one rounding is certain on every runtime; a 64-bit source is
  converted through a double on some, which rounds twice. `conv.i64.f64` holds 2 of P1-019's 238
  results.
- Deviation: two x87 sides were not runtime-sensitive (ADR 0040), so "never interpret a
  runtime-sensitive function" alone would have read their arithmetic as IEEE. A side whose floating
  point may run on x87 now names its functions `x87.f64.add` and so on, and no `x87.` function is
  interpretable (ADR 0053 decision 4). Their names in `properties.abstractions` change with it.
- Decision: a floating-point value in the IR -> the sort element whose id is its IEEE bits, with
  `IrSortValue.Id` widened to 64 bits. Before, a literal was a hash of its text and the IR did not
  hold `2.0`. Alternatives: a new `IrValue` kind (every consumer of values learns it); a constants
  side table on the procedure. Rule: 4. IR dumps print the bits where they printed the hash
  (`IrLowererSnapshotTests.PureCompoundAssignment`), and `--execute` now passes a model's
  floating-point input as the number it is.
- Decision: beyond the Design, a refined round that finds no divergence but an input reaching an
  opaque node reports Unknown(opaque) with ADR 0029's residual claim, marked `+refined`. The Goal's
  "Unknown for another reason" is this; the Design's "anything else keeps the Unknown it had" covers
  what is left. Alternatives: keep Unknown(abstraction) there too. Rule: 3.
- Decision: where what a refined round interpreted is recorded -> `LadderStep.Refined`, one step per
  round after rung 1's first. Alternatives: a property of `Verdict`; of `Equivalent` and `Divergent`
  each. Rule: 1 (it is how `LadderStep.Solver` carries ADR 0050's suffix).
- Decision: which sorts are floating point -> `IrFloat.Binary32` and `IrFloat.Binary64` in
  `Equiv.Core`, as `IrTuple` holds the tuple spelling. Alternatives: inferring it from each
  interpreted function's signature. Rule: 4.
- Decision: a model prints a floating-point value as its number (`f64 0.1`), in
  `properties.model`, `candidateCounterexample` and the message. Not asked for; without it criterion
  3's "a model that replays" reads as two 19-digit integers. Rule: 3.
- Decision: criterion 3's "a pair whose modern side calls a changed method on the result" ->
  `Arithmetic.Mean`, which converts its result with a user-defined conversion. A call is no
  abstraction, so a pair that only adds one is Divergent on the trace once the arithmetic is
  interpreted; an `op:` function is what a candidate can depend on and what the result then names.
  Alternatives: a shared opaque fragment after the arithmetic. Rule: 3.
- Decision: criterion 5's floating-point pairs -> a fourth family, `PairGen.FloatPair`, drawn apart
  from the others with its own `float g, double h` parameters, 50 pairs per pull request and 1,250
  nightly. Alternatives: adding the two parameters and types to every generated method, which moves
  what each seed draws (`BrokenIlSeed` pins one pair) and the signature seven other test classes
  spell. Its mutations are its own (IEEE identities against rewrites that differ on a NaN, a signed
  zero or a rounding), since the seeder's operators change no floating-point expression. Rule: 4.
- Criterion 5: `FloatingPointPairsAreSoundUnderBothLowerings` passed locally at the nightly budget,
  1,250 pairs under both lowerings, on 2026-10-07 (`EQUIV_DIFFERENTIAL_BUDGET=nightly`, 3 min 11 s).
  `RefinementSoundnessTests` passed once at 3,000 generated IR pairs (60 per run).
- Found by `Interpreter_AgreesWithZ3OnEveryEdgeValue`, before any verdict depended on it: Z3 5.1's
  `FPNum.ExponentInt64(biased: true)` gives an infinity an exponent one bit too wide (it decoded
  `+oo` as `2.0`), and throws on a NaN. `ModelDecoder` writes both out itself.
- The sample holds no `IntPtr` pair. On .NET Framework 4.8 against .NET 10 the legacy parameter is
  `System.IntPtr` and the modern one `nint`, so the two methods do not match (Added and Removed):
  that is P2-108, open. `Refined_IntPtrEqualityIsSortEquality` covers the operators in IR.
- Three samples (`webapi-basic`, `version-bump`, `runtime-row-framework-only-change`) fail to load
  in this worktree because their packages are not restored here; `build.ps1 -Integration` restores
  them, and CI is where their snapshots are checked.
- Criterion 6, measured 2026-10-07 on `gitextensions-9860` (compare mode quick, `--jobs 4`, `main` at
  628ef68f against this branch, one run each, 379 s and 349 s): **0 of 22** `abstraction` Unknowns are
  decided, and no result changed at all (14,044 results; Equivalent 13,734, EQ002 8, EQ006 7,
  Unknown 291 in both). The pair has 22 `abstraction` Unknowns today, not the 52 of the 2026-10-03
  run this ticket was written from, and none of the 22 names a floating-point function or an
  `IntPtr` operator: each depends on a `delegate:`, an opaque fragment, `get:System.String::get_Length()`
  or `op:GitExtUtils.ArgumentString::op_Implicit`. The pairs that held `conv.f32.f64`, `f64.mul` and
  the rest on 2026-10-03 have since been decided by other tickets. Quick, because refinement is part
  of rung 1 in the first pass, which both modes run; thorough's later passes were not measured.
- Criterion 4 and the yield where there is one, measured the same day and the same way on
  `gitextensions-8522` (388 s and 380 s): **12 of 178** `abstraction` Unknowns are decided, all
  Divergent, and no result that was Equivalent or Divergent changed (13,742 results; Equivalent
  12,727 in both). Six are EQ002 on `IntPtr ==`, the six of P1-019's report:
  `EasyHook.LocalHook::GetProcAddress(string,string)`, `::get_HookBypassAddress()`,
  `::IsThreadIntercepted(int)`, `::Dispose()`, `EasyHook.HookAccessControl::SetInclusiveACL(int[])`
  and `::SetExclusiveACL(int[])`. Six are EQ006, a runtime-changed callee in the trace:
  `GitUI.FontUtil::.cctor()` on `IntPtr !=`, and five on floating point
  (`GitUI.SpellChecker.EditNetSpell::GetCursorPosition()`,
  `GitExtensions.Plugins.GitStatistics.PieChart.PieChart3D::GetSliceDisplacement(float,float)`,
  `...PieChart.PieSlice::CreateBrushForSide(System.Drawing.Color,double)`,
  `ICSharpCode.TextEditor.TextAreaControl::HandleMouseWheel(System.Windows.Forms.MouseEventArgs)`,
  `ICSharpCode.TextEditor.TextView::GetFontHeight(System.Drawing.Font)`). P1-019's seventh,
  `FormStatus::BitmapToIcon`, is Equivalent by congruence on `main` today. None became
  Equivalent: as P1-019 read it, a shared function hides a divergence only where the sides already
  differ. Three results whose candidate names only floating-point functions stay Unknown (a refined
  query that gave up, or a conversion out of range), and three more name floating point beside
  something else. Of the 166 left, results holding each kind (a result can hold several): an opaque
  fragment 89, a `delegate:` 54, `get:System.String::get_Length()` 36, `string ==` or `!=` 19,
  `op:GitExtUtils.ArgumentString::op_Implicit` 17, floating point 6, `System.Type ==` 2.
- The raw SARIF and logs are under `.corpus/pairs/gitextensions-{9860,8522}/runs/20261007-*`, not in
  git (ADR 0028). No file under `docs/runs/` was added, so the README's scoreboard is untouched.
