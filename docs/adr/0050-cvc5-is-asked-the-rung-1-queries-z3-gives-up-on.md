# ADR 0050: cvc5 is asked the rung 1 queries Z3 gives up on

Status: accepted (2026-10-04). Supersedes ADR 0005's "Z3 alone": Z3 stays the encoder and the first
solver, and is no longer the only one.

## Context
Solver budget is the second largest Unknown reason on the three large runs, 400 of 1,471. P2-050
found that 20 times the budget decides 73 of 172 of them on Git Extensions and proves none. P1-025
asked the other question, whether another solver does better
(`docs/runs/2026-10-04-solver-portfolio.md`). On the 142 rung 1 queries Z3 gives up on, cvc5 1.4.1
answers 59 in the 60 s Z3 had: four unsatisfiable, which Z3 never answered on a timeout at any
budget, and 55 satisfiable, of which 9 replay to a Divergent and 27 to Unknown(abstraction).
It proves no pair. Bitwuzla 0.9.1 reads none of the files: the call trace uses datatypes, sequences
and integers. The three runs hold no rung 4 Unknown, so no Horn-clause solver was measured.

## Decision
A rung 1 query (`divergence`, `opaque`, `bound`) that Z3 gives up on is asked of cvc5, when cvc5 is
configured.

1. **Where it sits.** `Equiv.Verify.Z3` remains the one `IVerificationBackend` and the one encoder.
   `Equiv.Core` gains `ISmtSolver`: an SMT-LIB 2 script in, `sat` with the values asked for,
   `unsat`, or `unknown` out. `Equiv.Verify.Cvc5` implements it by running the `cvc5` executable
   as a process. It references `Equiv.Core` only. `Equiv.Verify.Z3` prints the query and reads the
   answer; it does not know which solver is behind the interface.
2. **What is sent.** The encoding's assertions and the query's terms as Z3 prints them from a plain
   solver, with two rewrites of the text, both of which leave the query's meaning as it is: a
   `seq.++` of one argument is its argument, and a constant array whose default is not a value is a
   fresh array constant constrained only where the query reads it. A query that still does not
   parse is the timeout it was.
3. **What an answer is worth.** A satisfiable answer is never a verdict. Its values are read back
   into a model and replayed in `IrInterpreter`, exactly as a model of Z3's is (ADR 0014, ADR 0026);
   only the replay makes a Divergent or an Unknown(abstraction). An unsatisfiable answer is trusted
   as Z3's is: it ends the query as unsatisfiable, and the rung goes on to its next query.
4. **How it is named.** A result one of whose queries cvc5 answered has `proofMethod` suffixed
   `+cvc5` (`bounded+cvc5`), as `+contract` is, and its `ladderTrace` step names the solver and its
   version. A result Z3 decided alone is named as today.
5. **Budget.** cvc5 gets `timeoutMs` of wall-clock time and its own resource limit (`--rlimit`),
   so that, as with Z3 (P2-050), the limit and not the clock ends most queries. The build ticket
   picks the limit by measurement.
6. **Distribution.** cvc5 is BSD-3-Clause, and its release binaries link GMP and LibPoly, which
   are LGPL-3.0. Under ADR 0017 that is a standalone executable run as a process and never
   redistributed: `equiv` ships no cvc5, in no binary, image or action. It runs the executable
   `equiv.config.json` names (`solvers.cvc5.path`), and without one behaves as today.
   `tools/cvc5/fetch.ps1` fetches the hash-pinned release for development and CI.
7. **Which queries go where.** Every query goes to Z3 first. Only rung 1's three queries go to
   cvc5, and only after Z3 gave up. Induction obligations, Horn clauses and contract queries stay
   with Z3 alone. Bitwuzla, Eldarica and Golem are not adopted.

ADR 0002 row, added by the ticket that builds this:

| Package | Version | Used by | Licence | Why this one |
|---|---|---|---|---|
| cvc5 (executable, not a package) | 1.4.1 | Verify.Cvc5, as a process | BSD-3-Clause; the release binary links GMP and LibPoly (LGPL-3.0), so it is never redistributed (ADR 0017) | the one other solver that reads the product encoding's theories (bit-vectors, arrays, functions, datatypes, sequences); measured by P1-025 |

## Why
- It is measured. 38 of 164 `timeout` Unknowns get another answer at no more wall-clock time than
  Z3 had. Nine are divergences a reviewer is today told nothing about.
- An unsatisfiable answer from Z3 was never seen on these queries. cvc5 gave four. Two of those
  pairs become Unknown(opaque) with the residual claim proved, which a timeout never has.
- A wrong `sat` from a second solver cannot produce a wrong verdict, because the replay decides. A
  wrong `unsat` can, as a wrong `unsat` from Z3 can today. P1-026 (proof certificates) is the check.
- One encoder. ADR 0005 rejected Boogie for the cost of a second IR; a second backend with its own
  encoding would cost the same. Sending the text Z3 prints costs a printer and a process.

## Rejected
- cvc5 as a second `IVerificationBackend` with its own encoder: two encodings to keep sound, for a
  solver that proved no pair.
- cvc5 through an API binding: there is no official .NET binding, and linking it would put LGPL
  code in the product, which ADR 0017 denies.
- Both solvers at once on every query: most queries Z3 answers in milliseconds, and a process per
  query would cost more than it saves. Out of P1-025's scope as well.
- Bitwuzla now: it reads no query until the trace is encoded without datatypes and sequences. P1-034
  measures that encoding; adopting Bitwuzla is a question for after it.
- Eldarica and Golem: no rung 4 Unknown on the three large runs to give them.
- Doing nothing because no pair is proved: the 5% bar does not gate this (P1-025 criterion 6), and
  the Divergents are real.

## Consequences
- P1-033 builds it. ARCHITECTURE.md's dependency rule gains `Equiv.Verify.Cvc5 --> Equiv.Core`, and
  VERIFICATION-MODEL.md's `proofMethod` text gains `+cvc5`; both change in that ticket.
- A run with cvc5 configured can give a different verdict from one without. The SARIF says which
  results cvc5 touched, and the run's properties name the solver and its version.
- Linux and the container image get no cvc5 until someone configures one. The Linux parity gate
  (ADR 0031) compares runs with the same solvers configured.
- No pair on Git Extensions becomes Equivalent by this. The timeouts that remain are an encoding
  question (P1-031, P1-034, P2-101), not a solver one.
