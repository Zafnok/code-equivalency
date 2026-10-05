# P1-037 Spike: can a model write a Lean proof, checked by Lean's kernel, for a query the solvers give up on?
Status: done (PR #406)
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-025

## Goal
ADR 0005 rejected Lean for the MVP: exporting gives a goal, and somebody still has to write the
proof. Trivet ("LLVM Translation Validation Automated with Large Language Models and Lean", arXiv
2609.19583) answers that for LLVM: a generator emits the theorem and a proof scaffold with typed
holes, a model fills the holes, and a proof counts only when Lean's kernel accepts it. It proves 10
fixed-width rewrites on which Alive2 times out, such as `(x /u y) * y <=u x` at 32 bits, at a mean
of 684 seconds a proof.

Trivet proves over a Lean semantics of LLVM's integer instructions, with no memory and no calls.
Writing a Lean semantics of our IR is far beyond a spike. This spike takes the shorter road: it
translates the query, not the program. A rung 1 `divergence` query is a quantifier-free formula
whose unsatisfiability is the proof; its negation is a Lean theorem over `BitVec`, `Bool`,
functions for arrays and uninterpreted functions, and `List` for the call trace. What Lean then
checks is exactly what Z3 checks today, so the trusted part (lowering and encoding) is unchanged
and only the solver is replaced, on the queries the solver cannot do.

Measure whether a model, inside a scaffold, proves any query Z3 gives up on. A model proposes and
the kernel admits, which is ADR 0036's rule. This is a spike. It changes nothing under `src/`.

## Spec references
ADR 0005, ADR 0036, ADR 0014, ADR 0050, ADR 0002 and ADR 0017 (Lean is Apache-2.0; nothing is
redistributed here), `tools/spikes/solver-portfolio/` (the export),
`docs/runs/2026-10-04-solver-portfolio.md`, `docs/runs/2026-10-01-timeout-budget.md`, P1-034,
P1-026 (the other certificate spike), `Equiv.Verify.Z3`'s invariant proposer for how a model is
called (`--invariant-model`, `ANTHROPIC_API_KEY`).

## Design
- `tools/spikes/lean-vc/`: a translator from the exported SMT-LIB files to one Lean 4 file each,
  `theorem q : ¬ (assertions ∧ query)` with every constant universally quantified. Bit-vector
  operators map to `BitVec`'s with SMT-LIB's semantics (division and remainder by zero included);
  arrays to functions with `store` as a function update; uninterpreted functions to variables of
  function type; the trace datatype to an inductive type and sequences to `List`.
- The scaffold does what needs no insight before a model is asked: introduce the hypotheses, split
  the top-level `ite` and disjunctions the encoding creates per path, discharge every branch
  `bv_decide`, `bv_omega`, `simp` or `omega` closes within a time limit, and leave the rest as
  holes.
- A model fills the holes, at most five rounds a query, each round given Lean's error. A proof is
  accepted only when the file compiles with no `sorry`, no axiom beyond Lean's standard three, and
  the theorem statement byte-identical to the generated one.
- A query whose answer may be satisfiable is not proved by anyone. The spike does not look for
  counterexamples: cvc5 and a larger budget already do.

Pitfalls.
- Size. The exported files are 120 KB to 10 MB. Record the largest file Lean elaborates in ten
  minutes, and do not count a file that does not elaborate as a failed proof: it is "too large".
- The translation is trusted. A wrong operator mapping proves a different theorem. Criterion 2 is
  the guard; do not weaken it.
- `bv_decide` is a SAT solver with a checked certificate. A proof it finds alone counts, and is
  reported apart from a proof that needed the model, because the first says Z3's tactics were the
  problem and the second says insight was.
- Most timeouts may not be theorems at all: cvc5's answers on these files were mostly satisfiable.
  That is a result, not a failure of the spike.

## Acceptance criteria (all must hold; nothing beyond them)
1. The translator's README lists every SMT-LIB operator in the 142 files with its Lean form, and
   the operators it does not translate. The report says how many of the 142 translate, and how
   many of those elaborate within ten minutes.
2. `--self-test`: on at least eight small queries Z3 does decide, half unsatisfiable, the Lean
   theorem is proved for each unsatisfiable one and, for each satisfiable one, Z3's model evaluated
   in Lean (`decide` or `#eval`) falsifies the theorem's body. Division by zero, signed and unsigned
   comparison, shifts past the width, a store and select, and an uninterpreted function are each
   in one of them.
3. A positive control outside our corpus: Trivet's example above, as a theorem at 32 bits, is
   proved through the same scaffold and model loop. If it is not, say so and stop: the pipeline
   does not reproduce the paper, and criteria 4 and 5 would say nothing.
4. Every translated query that elaborates is attempted. Per query: closed by the scaffold alone,
   closed with the model (rounds, seconds, tokens), not closed, too large. The four `divergence`
   queries cvc5 proved unsatisfiable are in the set and reported by name.
5. For each query closed, the report says whether the pair would then be Equivalent, counting rung
   1's later queries as P1-025 did.
6. `docs/runs/<date>-lean-vc.md` holds the tables and one line: the number of `timeout` Unknowns a
   kernel-checked proof decides, split by scaffold alone and with the model, and the mean cost of
   one. Identities and counts only; no query text and no proof text from a corpus pair.
7. If at least one query is closed with the model, write an ADR proposal against ADR 0005 through
   `equiv-adr` (Lean as a checker beside `ISmtSolver`, never redistributed, a new `proofMethod`; the model
   is sent query text, so by ADR 0049 decision 3 neither mode turns it on and it has its own
   option, as `--invariant-model` does) and the ticket it implies. If queries close by the scaffold
   alone and none with the model, write instead the ticket that tries the same tactics' idea in
   Z3 (case-split per path, then bit-blast), and no ADR. Otherwise one measured line in ROADMAP's
   post-MVP list.

## Files
`tools/spikes/lean-vc/**`, `docs/runs/<date>-lean-vc.md`, and either an ADR proposal and a ticket,
a ticket, or `docs/ROADMAP.md`. A Lean toolchain is installed under `.corpus/` by `elan` with a
pinned version recorded in the README; nothing of it is committed.

## Tests
The `--self-test` of criterion 2 and the control of criterion 3.

## Size guard
Any edit under `src/`: stop. A Lean semantics of `IrProcedure`, or a proof about the lowering or
the encoder: stop, that is a different and much larger project; record why the query-level road
was not enough. More than 1,500 lines in the translator: stop and re-read the Design.

## Out of scope
Loops beyond rung 1's unrolling. Proving the encoder correct. The other two large runs' timeouts.
Shipping Lean in any artifact.

## Notes
- Installing `elan` and a Lean toolchain changes the machine only under `.corpus/`; if it needs
  anything outside it, ask the user first.
- If P1-034 has landed, use its positional trace encoding: it removes `List` from the theorems and
  is likely to shrink them.
- Requested 2026-10-04. The paper review had ranked Trivet a drop because it needs a Lean
  semantics of the IR; translating the query instead is this ticket's answer to that.
- Result: `docs/runs/2026-10-04-lean-vc.md`. 139 of 142 translate, 121 elaborate, the scaffold
  alone closes none, the model closes 2 of the 5 it was asked (one kernel-checked, one through
  `bv_decide`). No pair becomes Equivalent. Criterion 7's first branch applies: ADR 0051
  (proposed) and P1-040.
- Deviation: the ticket admits a proof with "no axiom beyond Lean's standard three" and also says
  a proof `bv_decide` finds counts. In Lean 4.34.1 those cannot both hold: a `bv_decide` proof
  depends on a fourth axiom, `q._native.bv_decide.ax_...`, because its certificate is checked by
  compiled code. Such a proof is admitted, counted apart, and not called kernel-checked.
- Deviation: three queries (SMT-LIB files of 38, 151 and 152 MB) did not finish translating in two
  hours, twice. They are reported as not translated; the cause is the translator's speed on deep
  `let` nesting, not an operator.
- Decision: the translator is Python, not C#. It reads text files and needs nothing internal, and
  no gate builds a spike.
- Decision: the 142 positional files of P1-034's run are read as that run left them (equiv
  `ab66987`), per the note above; the sequence form, and so `List` and an inductive trace type, is
  not translated.
- Decision: the theorem is curried, one hypothesis `A = true` an assertion and `False` as the
  conclusion, in place of one `not (and ...)`. It says the same and needs no introduction step.
- Decision: `grind` joins the scaffold's tactics (congruence over uninterpreted functions, which
  `bv_decide` lacks), and the scaffold has no separate case-split step: `grind` and `bv_decide`
  split per path themselves, and a probe of explicit substitution and splitting on the smallest
  known theorem closed nothing.
- Decision: the model is called through the `claude` command line with no tools, since the box has
  no `ANTHROPIC_API_KEY`. Tokens are what it reports.
- Decision: the model is not asked about a query some solver answers satisfiable (110), nor about
  a statement over 1,500,000 characters (6).
- Decision: ADR 0051 and P1-040 carry a gate. Criterion 7 asks for them whenever a query closes
  with the model, and the evidence is that both closed queries are ones Z3 proves in the
  positional form.
- Surprising: `grind` does not finish in ten minutes on 1,088 hypotheses even with case splits,
  E-matching and arithmetic off. The cost is the size of the context, not the search.
- Surprising: on the three undecided queries it read, the model refused to write a proof and
  argued the query is satisfiable. Unchecked.
- The scaffold pass and the model pass each ran past the two-hour limit on a background command
  and were continued on the positions not yet reached; results are appended per query.
