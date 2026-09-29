# P1-009 Trace-mined coupling invariants: execution proposes, Z3 decides
Status: done (PR #251)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-002, P1-008; ADR 0036 accepted

## Goal
P1-002 lets a model propose a coupling invariant when rung 4 fails. This ticket adds a second,
cheaper proposer that needs no network. It runs both loops on inputs through `IrInterpreter`,
records the header variables at each aligned iteration, and proposes the conjunction of candidate
relations that held on every trace, in the style of Daikon. That is the data-driven equivalence
checking idea (Sharma, Schkufza, Churchill and Aiken, OOPSLA 2013). P1-002's rung checks the
candidate exactly as it checks a model's. By ADR 0036 a wrong candidate can only cost time.

## Spec references
ADR 0036 decision 1; ADR 0008 (ladder); P1-002 (`IInvariantProposer`, `LlmInvariantRung`);
VERIFICATION-MODEL.md section 5.1.

## Acceptance criteria (all must hold; nothing beyond them)
1. `TraceInvariantProposer : IInvariantProposer` in `Equiv.Verify.Z3`. It takes up to 200 inputs:
   the failed rung's counterexamples first, then random inputs over the header variables' sorts.
   It interprets both loop fragments in `IrInterpreter`, aligning iterations by count (lockstep),
   and collects header values.
2. The candidate templates over each pair of same-sort header variables (legacy x, modern y) are:
   `x = y`, `x = y + c`, `x = c*y` for small constants c seen in the traces, `x <= y`, and a
   per-variable range `lo <= x <= hi` when constant. The proposal is the conjunction of every
   template instance that held on all traces, emitted as SMT-LIB. With no surviving candidate it
   returns null.
3. The ladder tries `TraceInvariantProposer` before the LLM proposer. It is on by default, because
   it runs locally and sends nothing. A proven result has `proofMethod: trace-invariant` and
   `properties.proposedBy: "trace"`, and `properties.invariant` holds the admitted conjunction.
4. If Z3 rejects the full conjunction, the proposer drops, one at a time, the conjuncts falsified
   by the counterexample Z3 returned, and retries, within P1-002's `MaxRounds`.
5. On `loops/state-unpaired` (loops that do not align) with rung 4 forced to time out, the trace
   proposer alone proves the pair Equivalent. The snapshot is checked in. (Corrected from the
   `loop-fusion` sample; see the Deviation in Notes.)

## Files
`src/Equiv.Verify.Z3/Ladder/TraceInvariantProposer.cs`, `src/Equiv.Verify.Z3/Ladder/InvariantTemplates.cs`,
the ladder wiring file P1-002 created, tests, one snapshot.

## Tests
`Templates_KeepOnlyRelationsThatHoldOnEveryTrace`, `Proposer_ReturnsNullWithNoCandidate`,
`Proposer_DropsConjunctsFalsifiedByCounterexample`, `StateUnpaired_ProvedByTraceProposer` (snapshot),
`RandomWrongInvariantIsNeverAccepted` (reuse P1-002's property with this proposer).

## Size guard
More than 5 templates, or any template over three variables, is out of scope.

## Out of scope
Traces from real runtimes (P1-008's machinery; IR traces suffice here). Non-lockstep alignment.
Disjunctive invariants.

## Notes
- Deviation: criterion 5 cannot hold for `loop-fusion`. Once the fused side has returned, the relation of the old side's second loop (`inv.B6.exit`) must state `new.value + old.in = max(n, 0)`, and its reachable states are not convex: `(n, j, W) = (-100, 0, 0)` and `(5, 0, 5)` are reachable, and the integer point `(-79, 0, 1)` between them would let the loop exit with `j = 0 != W`. So no conjunction of linear relations (the templates, or any convex invariant) is inductive, and disjunctive invariants are out of scope. Criterion 5 now names `loops/state-unpaired`, whose loops rungs 2 and 3 cannot align whatever the timeout (as P1-002's fusion snapshot relies on); `CounterShape_ProvedByTheOffsetTemplate` and `Proposer_DropsConjunctsFalsifiedByCounterexample` (`loops/phis-reordered`) prove two more unaligned pairs, and `LoopFusion_NeedsMoreThanTheTemplates` pins fusion as Unknown(NoInvariant). `samples/loop-to-linq` (`loops/array-count`) is out of reach too: a thrown side's value is unconstrained, so equal return values need an implication.
- Decision: template pairs -> every pair of a relation's same-sort arguments (inputs and both sides), not only (legacy x, modern y). Alternatives: cross-side pairs only. Rule: 1 (rung 4's relations mix inputs and both states, and a fused loop needs `new.t = new.i`).
- Decision: "lockstep" alignment -> rung 4's own product schedule: both sides step when both return to their header or both leave it, else the returning side alone, and an exited side waits, so every sample is a state of the relation rung 4 checks. Alternatives: pairing the n-th header visits. Rule: 1.
- Decision: traces -> each side run segment by segment (`IrFragmenter.Segment`, cut as `ChcEncoder` cuts it) in `IrInterpreter`, the header state read off each cut event; `IrInterpreter` is unchanged. Alternatives: a header observer on `IrInterpreter` (a Core API change). Rule: 4.
- Decision: "range when constant" -> the lower (upper) bound is kept when every trace's minimum (maximum) is the same; a Bool's range is its value when it never changes. Alternatives: the overall min and max, which bound input-driven values. Rule: 3.
- Decision: small constants -> `|c| <= 64`; a factor is never 0 or 1; the offset and factor come from the first sample and must hold on all. Rule: 5.
- Decision: a relation no run reached is `false`; a counterexample concluding it drops that, leaving `true`. Rule: 1.
- Decision: criterion 4 -> each rejection's conclusion fact drops every conjunct its values falsify (a value that is neither an integer nor a Boolean falsifies nothing); an exit or timeout rejection drops nothing, and a candidate equal to a rejected one gives up. Rejections carry the broken rule's premise and conclusion facts (`InvariantRequest.Rejection.Premise`/`Conclusion`, from `ChcEncoder.Refutation`), which are also the counterexample inputs run first (their `in.*` values). Alternatives: parsing the reason text. Rule: 1.
- Decision: random inputs -> SplitMix64 from a fixed seed (the proposer takes another for the property test); bitvectors in [-4, 20], a sort's first three elements, maps with up to three entries; a run stops after 64 cut points, a segment after 10,000 steps. Rule: 5.
- Decision: exception ids in traces are interned in block order, old side first; a mismatch with `ChcEncoder`'s ids only costs a rejected conjunct. Rule: 5.
- Decision: rung 5 is one `LlmInvariantRung` per proposer with its `ProofMethod`; the ladder runs the trace proposer (`LoopLadder.Traces`, on by default, null in tests that isolate the model) after `ChcTimeout`, then the model after `ChcTimeout` or `NoInvariant`. So a pair rung 4 gave up on is now Unknown(NoInvariant) rather than ChcTimeout (`loops/loop-to-linq` fixture updated). Rule: 1.
- Toolchain: Z3's `IntNum.ToString()` prints a negative integer as `-5`, not SMT-LIB's `(- 5)`; `InvariantTemplates.Read` accepts both.
