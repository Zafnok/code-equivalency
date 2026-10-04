# P1-025 Spike: would a second solver decide the queries Z3 gives up on?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-050

## Goal
Solver budget is the second largest Unknown reason on the three large runs: 400 of 1,471. P2-050
showed that 20 times the budget decides 73 of 172 on Git Extensions and proves none. P2-101 asks
whether another Z3 tactic pipeline does better. This spike asks the other question: does another
solver. A portfolio runs several and takes the first answer. ADR 0005 chose Z3 alone; that choice is
open to change, and this spike finds which solvers to add and what their answers are worth.

Candidates: Bitwuzla and cvc5 for the product queries (bitvectors, arrays, uninterpreted functions),
Eldarica and Golem for rung 4's Horn clauses. Measure, and answer with counts.

## Spec references
ADR 0005 (direct Z3 encoding), ADR 0002 (dependency register), ADR 0017 (licence allowlist), ADR 0028
(the 5% bar), `docs/runs/2026-10-01-timeout-budget.md`, P2-101 (the same pairs, Z3 only),
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`), `src/Equiv.Verify.Z3/ChcEncoder.cs` (it already prints
SMT-LIB).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/solver-portfolio/` builds each `timeout` Unknown of `gitextensions-8522` as the
   P1-019 spike builds a pair, and writes the query that timed out as an SMT-LIB 2 file with its
   logic declared. It reports how many queries export, and why the others do not (a Z3-only
   construct in the assertion set).
2. Each exported file is run with Bitwuzla and cvc5, each given the wall-clock time Z3 had
   (`timeoutMs`). Per solver: unsatisfiable, satisfiable, unknown, timeout, error.
3. A satisfiable answer counts as decided only if its model, read back, replays through
   `IrInterpreter` as a Divergent or an Unknown(abstraction) does today. An unsatisfiable answer is
   counted as a proof and listed apart.
4. For every `chc-timeout`, `chc-spurious` and `no-invariant` Unknown of the three large runs, the
   Horn clauses are exported and run with Eldarica and Golem the same way. If there are fewer than
   ten such results, say so and skip the runs: rung 4 seldom applies to real code.
5. `docs/runs/<date>-solver-portfolio.md` holds the tables, each solver's version and licence as read
   from its own distribution, and one line: the number of `timeout` Unknowns some other solver proves
   or refutes. Identities and counts only.
6. Unless no other solver decides any query, write the ADR that supersedes ADR 0005's "Z3 alone" (a
   second solver behind `IVerificationBackend`, its `Equiv.Verify.<Solver>` project, its ADR 0002 row,
   how an answer from a solver other than Z3 is named in `proofMethod`, and which queries go to which
   solver) and the ticket that builds it, for the solver or solvers that decide the most. The 5% bar
   does not gate this: the repository is two weeks old and is not committed to one solver. Only if
   nothing is decided, add one measured line to ROADMAP's post-MVP list.
7. Nothing under `src/` changes. No solver binary is committed; the report names where each came
   from and its hash.

## Files
`tools/spikes/solver-portfolio/**`, `docs/runs/<date>-solver-portfolio.md`, and either
`docs/adr/NNNN-*.md` with its README row and a ticket, or `docs/ROADMAP.md`.

## Tests
A `--self-test` that exports one sample's query and checks Z3 answers the exported file as it
answered the in-memory one.

## Size guard
Translating the encoding for a solver (rewriting sorts, removing datatypes by hand) is the feature,
not the spike: export what Z3 prints and count what does not parse. Any edit under `src/`: stop.

## Out of scope
Running solvers in parallel inside `equiv`. String solvers (cvc5 strings, OSTRICH; ROADMAP post-MVP).
Proof certificates (P1-026).

## Notes
- From the 2026-10-03 improvement review (the "Solver portfolio" row).
- If P2-101 has landed, use its feature groups to pick which pairs to report first.
