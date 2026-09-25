# P1-009 Trace-mined coupling invariants: execution proposes, Z3 decides
Status: todo
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
5. On the `loop-fusion` sample with rung 4 forced to time out, the trace proposer alone proves the
   pair Equivalent. The snapshot is checked in.

## Files
`src/Equiv.Verify.Z3/Ladder/TraceInvariantProposer.cs`, `src/Equiv.Verify.Z3/Ladder/InvariantTemplates.cs`,
the ladder wiring file P1-002 created, tests, one snapshot.

## Tests
`Templates_KeepOnlyRelationsThatHoldOnEveryTrace`, `Proposer_ReturnsNullWithNoCandidate`,
`Proposer_DropsConjunctsFalsifiedByCounterexample`, `LoopFusion_ProvedByTraceProposer` (snapshot),
`RandomWrongInvariantIsNeverAccepted` (reuse P1-002's property with this proposer).

## Size guard
More than 5 templates, or any template over three variables, is out of scope.

## Out of scope
Traces from real runtimes (P1-008's machinery; IR traces suffice here). Non-lockstep alignment.
Disjunctive invariants.

## Notes
