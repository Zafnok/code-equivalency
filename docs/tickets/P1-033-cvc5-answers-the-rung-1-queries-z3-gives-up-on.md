# P1-033 cvc5 is asked the rung 1 queries Z3 gives up on
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-025

## Goal
P1-025 measured that cvc5 answers 59 of the 142 rung 1 queries Z3 gives up on for
`gitextensions-8522`, in the time Z3 had: 9 replay to a Divergent, 27 to Unknown(abstraction), and
four are unsatisfiable (`docs/runs/2026-10-04-solver-portfolio.md`). ADR 0050 decides how a second
solver sits behind the backend. When done, a run with cvc5 configured asks it each rung 1 query Z3
gave up on, and a run without it behaves exactly as today.

## Spec references
ADR 0050 (all seven decisions), ADR 0014, ADR 0026, ADR 0017 (LGPL: a process, never
redistributed), ADR 0030 (the hash-pinned fetch this copies), `docs/runs/2026-10-04-solver-portfolio.md`,
`tools/spikes/solver-portfolio/` (`SmtFile`, `ReadBack`, `External`: the shape, not the code to
keep), `src/Equiv.Verify.Z3/LoopLadder.cs` (`Bounded`, `WithinBound`), `Z3Backend.cs` (`Query`,
`Check`), `ModelDecoder.cs`.

## Design
- `Equiv.Core`: `ISmtSolver` with one method, a script and a wall-clock limit in, an answer out
  (`sat` with the values of the constants asked for, `unsat`, `unknown` with a reason). The solver's
  name and version are properties. `VerificationOptions` carries an optional `ISmtSolver`.
- `src/Equiv.Verify.Cvc5`: implements it by running the configured executable with `--rlimit`,
  `--tlimit` and `--arrays-exp`. It references `Equiv.Core` only. The process is killed at the
  wall-clock limit. No cvc5 file is in the repository or in any artifact.
- `Equiv.Verify.Z3`: when `Z3Backend.Check` answers unknown on a rung 1 query and a solver is
  given, print the query (assertions and terms from a plain solver, not the inlined form, which
  prints tens of times larger), apply ADR 0050 decision 2's two rewrites, and ask. `unsat` is the
  query's answer. `sat` is read back: build the model from the values (Z3 completes the functions
  under the same limits) and replay as today. A read-back that fails leaves the query a timeout.
- `equiv.config.json`: `solvers.cvc5.path`. Absent, nothing changes.
- `tools/cvc5/fetch.ps1`: the release archive by version and SHA-256, into a git-ignored folder, as
  `tools/z3-feed/fetch.ps1` does. CI's Windows leg runs it for the integration test.

Pitfalls.
- The constant-array rewrite must be exact. `((as const (Array K V)) d)` with `d` not a value
  becomes a fresh array `a` with `(select a i) = d` asserted for every index term `i` the query
  reads from an array built on it and no store in between covers. If that set cannot be computed
  from the term, do not send the query. Do not send an under-constrained array: an `unsat` would
  still be sound, but a `sat` would cost a failed read-back each time.
- `sat` is never a verdict. A test must show a model that does not replay leaves the pair a timeout.
- The ladder's order is unchanged: an unsatisfiable `divergence` from cvc5 goes on to `opaque`,
  which is asked of Z3 first and of cvc5 if Z3 gives up.
- Identifiers Z3 prints in `|...|` can hold any character but `|` and `\`. Read values back by
  name through the script's own `get-value`, never by position.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ISmtSolver` is in `Equiv.Core`; `Equiv.Verify.Cvc5` references only `Equiv.Core`; the
   architecture tests pin both, and that `Equiv.Verify.Z3` does not reference `Equiv.Verify.Cvc5`.
2. With no solver configured, every sample and every existing test gives the result it gives today.
3. With a fake `ISmtSolver`: an `unsat` on `divergence` continues to `opaque`; a `sat` whose values
   replay to a divergence is Divergent; a `sat` that replays to no difference, an `unknown`, a
   malformed answer and a solver that throws each leave Unknown(timeout).
4. A result cvc5 touched has `proofMethod` suffixed `+cvc5` and a `ladderTrace` step naming the
   solver and version (snapshot test). VERIFICATION-MODEL.md and ARCHITECTURE.md say so.
5. One integration test, skipped when no cvc5 is configured and run on CI's Windows leg, asks the
   real cvc5 a query from a new sample `hard-for-z3` that Z3 gives up on at the default budget and
   cvc5 answers.
6. ADR 0002 has the row ADR 0050 gives. `tools/licence-check` passes; if it needs a `policy.json`
   entry for an executable that is not a package, the PR says so.
7. The cvc5 resource limit is chosen from a run of `gitextensions-8522`'s `timeout` Unknowns at
   three limits, recorded in `docs/runs/<date>-cvc5-budget.md` with, per limit, the answers and how
   many the wall-clock ended. The same file reports the pair's `timeout` Unknowns with and without
   cvc5.

## Files
`src/Equiv.Core/**` (`ISmtSolver`, options, config), `src/Equiv.Verify.Cvc5/**`,
`src/Equiv.Verify.Z3/{LoopLadder,Z3Backend,ModelDecoder}.cs` and one new file for the printer and
read-back, `src/Equiv.Cli/**` (wiring), `tools/cvc5/fetch.ps1`, `samples/hard-for-z3/**`,
`docs/adr/0002-dependencies.md`, `docs/ARCHITECTURE.md`, `docs/VERIFICATION-MODEL.md`,
`Equiv.slnx`, `.github/workflows/**` (the fetch step), matching tests.

## Tests
Unit tests for the printer's two rewrites (a property test: Z3 answers the rewritten text as it
answers the original, on generated pairs), the answer parser, and criterion 3's cases. Snapshot for
criterion 4. Architecture tests for criterion 1. The integration test of criterion 5.

## Size guard
If the diff touches a rung other than rung 1, or gives cvc5 an encoder of its own, stop: ADR 0050
decision 7 keeps both out. If the constant-array rewrite grows past one file, send only the queries
that have no such array and record the count in Notes.

## Out of scope
Running the solvers in parallel. Bitwuzla (P1-034 first). Proof certificates (P1-026). cvc5 on
Linux or in the container image.

## Notes
- From P1-025. The spike's read-back asks Z3 to complete the model and loses 19 of 55 satisfiable
  answers that way; decoding the functions cvc5's model gives would lose fewer, and is a follow-up
  only if criterion 7's run shows the loss is still large.
