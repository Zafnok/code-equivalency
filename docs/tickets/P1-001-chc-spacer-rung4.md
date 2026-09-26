# P1-001 Loop ladder rung 4: constrained Horn clauses via Z3 Spacer
Status: in-progress
Effort: L
Model: Opus, max effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-003

## Goal
Loop pairs that rungs 1 to 3 leave as Unknown(unaligned-loop) are handed to Z3's
fixedpoint engine, which synthesises the coupling invariant itself. Success gives
unbounded Equivalent with `proofMethod: chc`; a derivation gives Divergent (after
replay); timeout gives Unknown(chc-timeout).

## Spec references
VERIFICATION-MODEL.md section 5.1 rung 4; ADR 0008; M3-002 design (reuse
`IrFragmenter`; the fragments are the same, the obligations change).

## Design

Reference construction: Felsing, Grebing, Klebanov, Rümmer, Ulbrich, "Automating
regression verification" (ASE 2014, the Rêve tool). Encode the pair as a CHC system
with one unknown relation per loop pair and let Spacer find it.

For a loop pair (L_old, L_new) with header states s_old, s_new (all live header
variables, scalars and maps), declare `Inv(s_old, s_new)` as a relation
(`ctx.MkFuncDecl(name, sorts, BoolSort)` registered with `fixedpoint.RegisterRelation`).
Clauses (each `fixedpoint.AddRule` on a universally quantified implication):

1. Init: `prefix_old(in, s_old) AND prefix_new(in, s_new) -> Inv(s_old, s_new)`.
2. Lockstep step: `Inv(s_old, s_new) AND g_old(s_old) AND g_new(s_new) AND
   body_old(s_old, s_old') AND body_new(s_new, s_new') -> Inv(s_old', s_new')`.
3. Old-only step: `Inv AND g_old AND NOT g_new AND body_old -> Inv(s_old', s_new)`.
4. New-only step: symmetric. Clauses 3 and 4 are what make unbalanced loops (fusion,
   different trip counts, loop-to-iterator rewrites) provable.
5. Query: `Inv AND NOT g_old AND NOT g_new AND suffix_old AND suffix_new AND
   observables differ -> false`. Also a query for "one side exited, the other loops
   forever" is out of scope (partial equivalence only).

`fixedpoint.Query(...)`: UNSAT of the query means Spacer found `Inv`; read it with
`fixedpoint.GetAnswer()` and store its string in `properties.invariant` so the SARIF
result shows the proof. SAT means a derivation exists: extract the initial inputs
from `fixedpoint.GetAnswer()` (the refutation) and replay through `IrInterpreter`
with a large step budget; report Divergent only if the replay diverges, else
Unknown(chc-spurious) with both traces in the message.

Parameters: `fixedpoint.Parameters` with `engine=spacer`, `timeout`,
`spacer.q3.use_qgen=true` when maps are present, `fp.spacer.ground_pobs=false`.

Bitvectors: Spacer's invariant inference is much stronger over linear integer
arithmetic than over bitvectors. Add `--chc-int-mode` (default on): translate
`IrBitVec` to `IntSort` with bounds assumptions, and separately prove with rung 1
that no `IrOverflows` is reachable in the loop (if it is, stay in bitvector mode).
Record the mode in `properties.chcMode`.

Fragments: `IrFragmenter` from M3-002 yields prefix, body, suffix per loop; encode each
fragment as a Z3 formula over named inputs and outputs by reusing `ProductEncoder`
internals (extract a `FragmentEncoder` that returns a `BoolExpr` plus its interface
variables instead of asserting into a solver). Nested loops: inner pairs get their own
`Inv` relations and the outer body clause references them; do not summarise with
uninterpreted functions here as M3-002 does.

## Deliverables
- [ ] `FragmentEncoder` extracted from `ProductEncoder` without changing M3-001 behaviour
      (snapshot tests must not change).
- [ ] `ChcEncoder`, `SpacerRung`, `IntModeTranslator`, wired into `LoopLadder` after rung 3.
- [ ] Two new samples: `loop-to-linq` (for over an array vs `Sum()` is opaque, so use a
      manual `while` with a different counter shape) and `loop-fusion` (two loops merged
      into one). Both must reach unbounded Equivalent on this rung; snapshots include
      the invariant string.
- [ ] Fixtures: a pair that is refuted only on this rung (differing trip count with
      identical bodies), a spurious-derivation case forced by int-mode, timeout case.
- [ ] Property tests: soundness harness and ladder monotonicity extended to rung 4.
- [ ] Benchmark note in `## Notes`: wall-clock per sample on this box.

## Acceptance criteria (all must hold; nothing beyond them)
1. The two new samples reach Equivalent with `proofMethod: chc` and a non-empty
   `properties.invariant`, in under 30 seconds each on this box (time in Notes).
2. `Unknown(UnalignedLoop)` is no longer a final verdict; it becomes `chc`
   Equivalent, Divergent (replayed), or `Unknown(ChcTimeout)`.
3. M3-001 snapshot tests are byte-identical after the `FragmentEncoder` extraction.
4. Int-mode is used only when a rung-1 proof shows no reachable `IrOverflows`; a test
   forces an overflowing loop and asserts bitvector mode was used (`properties.chcMode`).
5. A spurious Spacer derivation (constructed via int-mode on a fixture) yields
   `Unknown(ChcSpurious)` with both traces in the detail, never Divergent.

## Size guard
Four new files. If you are writing your own invariant inference or abstract domains,
stop: Spacer does that.

## Pitfalls
- Spacer needs every relation's arguments to be variables, not terms, in rule heads;
  introduce fresh variables and equalities in the body.
- Do not mix `Solver` and `Fixedpoint` objects across the same `Context` unless both
  are disposed properly; create a fresh `Context` for the CHC attempt.
- Map (array) sorts inflate Spacer's search; try scalar-only loops first in tests.
- Keep int-mode sound: the overflow-freedom check must be a rung 1 proof, not an
  assumption.

## Out of scope
Rung 5 (P1-002). Any change to rungs 1 to 3 beyond the encoder extraction.

## Notes
- Deviation: integer mode's overflow freedom is proved by a Spacer query (each side's own Horn clauses: one relation per header, `bad` when a segment overflows an operation the translation keeps exact, inputs within their bounds), not by rung 1. A rung 1 result is a proof only when no input passes its bound, and every pair that reaches rung 4 has such an input (otherwise rung 1 decided it), so a rung 1 overflow check could never enable integer mode. Criterion 4 and the Design are corrected to "a Spacer overflow query"; the check is still a proof, never an assumption (Pitfalls). Needs your decision.
- Deviation: rung 4 applies only to a pair neither side of which holds an `IrCall` or `IrPure`. The call trace is an observable, and a sequence of events with results that depend on the call's position is not something a Spacer relation can hold (no sequence theory; uninterpreted functions in clause bodies are not supported). Such a pair keeps `Unknown(UnalignedLoop)`, with rung 4 `not-applicable` in its ladder trace naming the call; criterion 2 is amended to "for a pair rung 4 applies to". Needs your decision.
- Decision: cut points instead of per-loop-pair prefix/body/suffix -> each side is cut at every loop header with `IrFragmenter.Segment` (as rung 2 does, M3-002 Notes), and there is one relation `inv.<a>.<b>` per old cut point `a` and new cut point `b` (a header or the exit). A step runs one segment on one or both sides under Rêve's scheduler: both step when both segments return to their header or both leave it, else only the side that loops steps, and an exited side waits. Every pair of terminating runs is then one path of steps, so no loop pairing is needed, which is what unaligned loops (fusion: two loops against one) require; nested loops need no summaries. Alternatives: the Design's one relation per loop pair with prefix, body and suffix (needs the loops paired first, which is exactly what fails for these pairs). Rule: 4.
- Decision: relation arguments -> the shared inputs and the sort literals, carried unchanged (a derivation's facts then give the inputs to replay, and a literal keeps one identity along a derivation; both are universally quantified per rule), then each side's state: a header's `IrFragmenter.State` without its parameters (inputs) and without variables a constant defines (bound to that literal, which also keeps a product by it linear), and at the exit the observables (returned, value, exception id, by-ref finals). Alternatives: header state only (a derivation's inputs are then lost). Rule: 1.
- Decision: integer mode translation -> exact for add, sub, neg, mul by a constant, sdiv/srem/udiv/urem by a non-zero constant, signed and unsigned comparisons (a negative integer read unsigned is itself plus 2^w), bitwise not (-x-1), sign and zero extension and truncation (mod 2^w); every other bitvector operation (and, or, xor, shifts, a product of two unknowns, division by an unknown) is any integer within its width's bounds, as is every bitvector read from a map. Only exact add/sub/mul/neg/sdiv can overflow. Alternatives: exact non-linear products (Spacer's non-linear support is weak). Rule: 3.
- Decision: the divergence query assumes no input bounds; only the overflow query does. Integers out of bounds only add runs, so a proof stays a proof and a derivation is replayed anyway; on the fusion probe the bounds made Spacer 4 times slower. Rule: 4.
- Decision: Spacer parameters -> the ticket's `engine=spacer`, `timeout`, `spacer.ground_pobs=false`, `spacer.q3.use_qgen` when a relation holds a map, plus `spacer.global=true` and `spacer.gg.concretize=false`. Without global guidance (Krishnan et al., CAV 2020) the fusion pair timed out after 20 s in both queries; with it the overflow query took 0.34 s and the divergence query 0.55 s. `ground_pobs` makes no measurable difference once global guidance is on. Rule: 1.
- Decision: a derivation's inputs -> the first ground fact of an `inv.*` relation in the refutation (`GetAnswer()` on a satisfiable query is a hyper-resolution proof), with `xform.inline_eager`, `xform.inline_linear` and `xform.slice` off so that every relation a derivation passes through leaves such a fact with all its arguments. With inlining on, Z3 inlined a loop pair whose self-steps it had simplified away (a generated loop guarded by `i <u 0`); with slicing on, the proof cited `inv.B1.B1!slice!2`, a copy without the inputs no rule read. Turning them off cost nothing measurable on the fixtures (fusion 0.54 s either way). A derivation without a fact then comes from a rule without a relation, which only an entry segment reaching an opaque node has, and its inputs are a model of that. Alternatives: a `start(inputs)` relation (Z3 removes relations that always hold, whatever the `xform.*` settings), the `query!0` fact (Z3 permutes its arguments). Rule: 4.
- Decision: `properties.invariant` -> each relation Spacer defines as other than `false`, as `old <a> ~ new <b>: <definition>` with the arguments named `in.*`, `lit.*`, `old.*`, `new.*` (Spacer's own answer names them `A`, `B`, ... by position, which a reader cannot map back). Rule: 1.
- Decision: `properties.chcMode` -> `LadderStep.Mode` on rung 4's step, written from the ladder whatever the verdict (an Unknown(ChcTimeout) needs it too); `Equivalent.Invariant` carries the invariant. `ChcMode` members are `Integers` and `BitVectors` (CA1720 rejects `Int` and `Integer`); SARIF spells them `int` and `bitvector`. Rule: 2.
- Decision: a derivation whose replay reaches an opaque node is `Unknown(Opaque)` with that node as the cause, not ChcSpurious: a real input reaches it (ADR 0014). A spurious integer-mode derivation is not retried over bitvectors (criterion 5 expects ChcSpurious, and bitvector Spacer rarely proves what integer Spacer could not). Rule: 3.
- Decision: `LadderPropertyTests`' looping-mutant soundness property counts a kept mutant against Equivalent only when its witness makes both runs terminate. Every rung proves partial equivalence (VERIFICATION-MODEL.md section 5.1: termination is not claimed), and rung 4 is the first rung strong enough to prove a pair whose mutant differs only by never exiting (a flipped loop guard, found at seed `5FKftZEP-VMd`), which the harness had kept because the interpreter's budget ran out on one side. Rule: 3.
- Decision: six M3-002 fixtures change verdict because rung 4 decides them, as criterion 2 requires: `irreducible`, `nesting-changed`, `state-unpaired` and `phis-reordered` are `Equivalent(chc)` (rung 4 cuts at every back-edge target, so irreducible flow needs no alignment), `late-divergence-beyond` is `Divergent(chc)` (replayed), and `loop-to-linq` (sums, so integer mode is out) is `Unknown(ChcTimeout)`, the timeout fixture. `ALoopOnOneSideOnlyClimbsTheLadderAndIsUnaligned` becomes `...ToRungFour`: a loop that never exits against a straight body is partially equivalent. Rule: 3.
- Toolchain: `Fixedpoint.Query` does not return UNKNOWN at its timeout; it throws `Z3Exception` with message `canceled` (or `push canceled`). With `spacer.global=true` and the default `spacer.gg.concretize=true`, a timeout instead throws `unreachable` after printing "UNEXPECTED CODE WAS REACHED." (deterministic on `loops/loop-to-linq`); turning concretize off restores the cancellation and keeps the speed.
- Toolchain: `Fixedpoint.GetCoverDelta(-1, r)` on a relation Spacer sliced away kills the process with an access violation (0xC0000005). Not used; the invariant is read from `GetAnswer()`.
- Toolchain: a Z3 constant that is a registered nullary relation (`bad`) must not be among a rule's bound constants, or Z3 rejects the rule with "Illegal head ... (= (:var 0) true)".
