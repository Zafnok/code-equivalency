# P1-001 Loop ladder rung 4: constrained Horn clauses via Z3 Spacer
Status: todo
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
