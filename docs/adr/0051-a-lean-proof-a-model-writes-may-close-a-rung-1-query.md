# ADR 0051: A Lean proof a model writes may close a rung 1 query no solver decides

Status: proposed (2026-10-04). Would narrow ADR 0005's rejection of Lean: Lean stays out of the
encoding and checks one kind of claim.

## Context
ADR 0005 rejected Lean because an exported goal still needs someone to write the proof. P1-037
measured that on the 142 rung 1 queries Z3 gives up on for `gitextensions-8522`
(`docs/runs/2026-10-04-lean-vc.md`), each translated to one Lean theorem over `BitVec`, `Bool`,
functions and type variables. 121 elaborate within ten minutes. No tactic that needs no insight
(`bv_decide`, `grind`, `simp_all`, `omega`, `bv_omega`) closes one. A model, given the theorem and
Lean's errors, closes two of the five it could be asked: one with Lean's three standard axioms, one
through `bv_decide`. Both are queries Z3 itself proves once the trace is compared by position
(P1-034), neither pair becomes Equivalent, and none of the three queries no solver decides was
closed. P1-037's criterion 7 asks for this proposal whenever a query closes with the model.

## Decision
A rung 1 query that every configured solver gives up on may be closed as unsatisfiable by a Lean
proof, when the user turns that on.

1. **Where it sits.** `Equiv.Core` gains `IProofChecker`: a theorem and a proof in, admitted or
   rejected out. `Equiv.Verify.Lean` implements it by running the `lean` executable as a process and
   references `Equiv.Core` only. `Equiv.Verify.Z3` stays the one encoder: it prints the query as it
   does for `ISmtSolver` (ADR 0050 decision 2), in the positional form (P1-038).
2. **What is translated.** The query, never the program: each declared sort a type variable, each
   constant a binder, each assertion a hypothesis, the conclusion `False`. There is no Lean
   semantics of the IR, the lowering or the encoder. The translator is trusted as the encoder is,
   and its operator table is tested against Z3 on every operator it emits.
3. **Who proposes, who admits (ADR 0036).** A model writes the proof, at most five rounds, each
   given Lean's error. A proof is admitted only when Lean compiles the generated statement with it,
   with no `sorry` and no axiom beyond `propext`, `Classical.choice` and `Quot.sound`. A proof that
   uses `bv_decide` depends on a further axiom (its certificate is checked by compiled code, not
   the kernel) and is admitted under its own name, decision 5.
4. **What an admitted proof is worth.** It ends that query as unsatisfiable, as an `unsat` from a
   solver does, and the rung goes on to its next query. It is never a verdict by itself, and a
   failure to prove says nothing: the query is the timeout it was.
5. **How it is named.** `proofMethod` is suffixed `+lean` when every admitted proof used the three
   axioms only, and `+lean-bv_decide` otherwise. The `ladderTrace` step names Lean's version, the
   model id and the rounds.
6. **Consent and distribution.** The model is sent the query's text, which holds names and
   constants from the analysed code. By ADR 0049 decision 3 neither mode turns it on: it has its
   own option, `--proof-model`, as `--invariant-model` does. Lean is Apache-2.0 and is run as a
   process the config names (`provers.lean.path`); `equiv` ships no Lean (ADR 0017).
7. **It is not built until it decides something a solver does not.** P1-040 starts by measuring
   the queries still undecided after P1-038 and P1-033 are in. If no such query is closed, P1-040
   stops there and this ADR is withdrawn.

## Why
- The objection in ADR 0005 is answered in part: a model wrote a 56-line proof of a query with
  1,088 hypotheses in one round, and Lean's kernel accepted it.
- A wrong proof cannot be admitted, which is not true of a wrong `unsat` from a solver. The
  translator is the only new trusted part.
- Decision 7 is there because the measured yield is nothing: both closed queries are ones Z3
  proves in milliseconds in the positional form, at a mean of 781 seconds and 541,777 tokens a
  proof here. On the three undecided queries it read, the model wrote no proof and argued in every
  round that the hypotheses are satisfiable.

## Rejected
- A Lean semantics of `IrProcedure` (Trivet's road for LLVM): a second semantics to keep equal to
  the lowering and the encoder, which ADR 0005 already refused for Boogie.
- The scaffold alone, with no model: it closes 0 of 121. `grind` reaches its time limit on 100.
- The same tactics' idea in Z3 (case-split per path, then bit-blast): criterion 7 asks for that
  only when the scaffold closes a query, and it closed none.
- Counting a `bv_decide` proof as kernel-checked: it carries an axiom the kernel does not check.
- Building it now behind the option: two proofs of what P1-038 gives without a prover do not pay
  for a component, a translator and a third executable.

## Consequences
- P1-040 holds the gate and, past it, the build. Nothing is scheduled before P1-038 and P1-033.
- If built: ARCHITECTURE.md's dependency rule gains `Equiv.Verify.Lean --> Equiv.Core`,
  VERIFICATION-MODEL.md's `proofMethod` text gains the two suffixes, and ADR 0002 gains a row for
  the Lean executable.
- Size bounds it whatever the proof: of the 142, 3 did not translate in two hours, 18 did not
  elaborate in ten minutes (the largest that did is 17 million characters), and 6 more are over
  the 1.5 million characters the model was given.
- No pair on Git Extensions becomes Equivalent by this.
