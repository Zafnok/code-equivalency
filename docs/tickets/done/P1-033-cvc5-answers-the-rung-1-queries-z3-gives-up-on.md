# P1-033 cvc5 is asked the rung 1 queries Z3 gives up on
Status: done (PR #400)
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
- Decision: a Divergent or an Unknown cvc5 answered a query of gets `proofMethod: <rung>+cvc5`,
  though neither carries a `proofMethod` otherwise. ADR 0050 decision 4 says "a result", and the
  `+contract` precedent only covers an Equivalent. A Divergent the runtimes showed stays `observed`.
- Decision: the step is tagged (`LadderStep.Solver`, a `SolverUse` of name and version) only when
  cvc5's answer was used: an `unsat` taken, or a `sat` Z3 read back to a model. An answer that leaves
  the timeout tags nothing, so a result that says `+cvc5` is one cvc5 changed.
- Decision: the constant-array rewrite works on Z3's terms before printing, not on the text, and
  asserts the default at every index read from an array built on the constant array, whether or not
  a store in between covers it. The extra constraints are true of the constant array, so the rewrite
  is still exact; pruning them would need an index comparison for no gain. It fits in
  `SecondSolver.cs`, so the size guard's fallback was not needed.
- Decision: a `sat` on `opaque` or `bound` is read back through Z3 as one on `divergence` is, so no
  rung 1 answer rests on cvc5's `sat` alone.
- Decision: the read-back takes a value only as a literal of the constant's own sort and width
  (`true`, `false`, `#b`, `#x`) and needs one for every constant asked for; anything else is the
  timeout. Names are matched as symbols, with or without `|...|`: cvc5 prints `|in.a|` back as `in.a`.
  CI's first run caught that, in the integration test.
- Decision: criterion 3's "a `sat` that replays to no difference" is tested with values on which the
  sides agree (every constant zero). Z3 rejects them at the read-back, before any replay. A model Z3
  itself confirms and that then replays to no difference is the encoder bug it was before, and still
  fails loudly.
- Decision: `--rlimit` 2,000,000 (`docs/runs/2026-10-04-cvc5-budget.md`). It gets the answers there
  are to get, 58 of 95 scripts against 34 at a quarter of it. It does not meet ADR 0050 decision 5 in
  full: the wall-clock limit ends 23 of the 37 scripts cvc5 gives up on.
- Decision: the process is killed 5 s after `--tlimit`, not at it: cvc5 checks its own limit between
  steps and then says why it stopped, which the run needs to tell the clock from the resource limit.
- Deviation: files outside the Files list. `tools/spikes/cvc5-budget/` is criterion 7's measurement
  tool (throwaway, not in `Equiv.slnx`, as P1-025's is). `tests/Equiv.Verify.Cvc5.Tests/`, the
  `mutation.yml` leg, `docs/QUALITY-GATES.md` and `.gitignore` follow from a new `src/` project and
  a fetched binary. `README.md` gains one paragraph on the setting.
- Deviation: ADR 0050's Consequences say the run's properties name the solver and its version. No
  criterion asks for it and it is not built; each result cvc5 touched names both in its step.
- The 49 `timeout` Unknowns that send cvc5 nothing were not split into "constant array used as a
  whole" and "timeout on a later rung"; the tool does not record why nothing was sent.
- `tools/licence-check` needs no `policy.json` entry: cvc5 is in no lock file, so the tool never sees it.
- The new required check `stryker (Equiv.Verify.Cvc5, Equiv.Verify.Cvc5.Tests)` has to be added to
  the branch ruleset by hand.
- Z3's `Expr.ToString()` prints a bit-vector numeral in decimal, so a test solver built on Z3 has to
  format `#b` literals itself. `cvc5 --version` starts `cvc5 1.4.1 [git ...]`; the word "version"
  on its second line is the compiler's.
