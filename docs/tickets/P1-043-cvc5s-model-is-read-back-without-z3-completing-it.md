# P1-043 cvc5's satisfiable answer is read back from its own model, without Z3 completing it
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-033

## Goal
ADR 0050 decision 3 never takes cvc5's `sat` as a verdict: its values are replayed. Today
(P1-033) only the values of the query's Bool and bit-vector constants are read; they are asserted
beside the query and Z3 is asked to complete the functions, arrays, datatypes and sequences. When Z3
gives up on that, the answer is lost and the query stays the timeout it was.

P1-039's thorough runs of `gitextensions-8522` (`docs/runs/2026-10-14-cvc5-partitions.md`) show it
is now the largest reason a `timeout` Unknown stays one after cvc5 is asked. Of the pair's 22
`timeout` Unknowns, 7 have a rung 1 query cvc5 answers `sat` in seconds whose model Z3 cannot
complete, among them `AppearanceSettingsPage::SettingsToPage()`,
`SubmoduleHelpersTest::GetSubmoduleNamesFromDiffTest()` and `AppSettings::.cctor()`. With
`solvers.cvc5.partitions` set, the rung 1 query of `GitModule::GetInteractiveRebasePatchFiles()`, an
Unknown(`unaligned-loop`) whose rung 1 times out, gets a satisfiable partition that is lost the same
way. P1-025 and P1-036 lost answers to the same limit.

When done, a `sat` from cvc5 whose values Z3 cannot complete is read back from cvc5's own model
(`get-model`), decoded into the model `ModelDecoder` replays, and replayed as any other. A model
that cannot be decoded is still the timeout it was. Nothing is a verdict without the replay.

## Spec references
ADR 0050 (decision 3), ADR 0014, ADR 0026, `src/Equiv.Verify.Z3/SecondSolver.cs`,
`src/Equiv.Verify.Z3/ModelDecoder.cs`, `src/Equiv.Verify.Cvc5/SmtOutput.cs`,
`docs/runs/2026-10-04-solver-portfolio.md`, `docs/runs/2026-10-14-cvc5-partitions.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. When the read-back through Z3 does not satisfy the query, cvc5's full model is asked for and
   decoded. Every sort the product encoding uses that cvc5 prints a value of is decoded or makes the
   model undecodable; none is guessed.
2. A decoded model is checked against the query before it is replayed: Z3 evaluates the query's
   assertions under it, with every constant and function fixed, and only a model that satisfies them
   is replayed. ADR 0050 gets a dated clarification that says so.
3. On `gitextensions-8522` in thorough mode with cvc5 configured, the run file reports what each of
   the 7 results above becomes (and, with `solvers.cvc5.partitions` 8, `GetInteractiveRebasePatchFiles`),
   and that no result Z3 or the first read-back decided changes.

## Files
`src/Equiv.Verify.Z3/**`, `src/Equiv.Verify.Cvc5/**`, tests,
`docs/adr/0050-cvc5-is-asked-the-rung-1-queries-z3-gives-up-on.md` (clarification),
`docs/runs/<date>-cvc5-model.md`.

## Tests
Unit tests of the model reader per sort (a function table, a constant array, a store chain, a
datatype value, a sequence), a model that does not satisfy the query, and one that cannot be read.

## Size guard
Replaying without Z3's check of criterion 2, or a second decoder for Z3's own models: stop.

## Out of scope
Queries cvc5 does not answer (P2-196, P1-039). Certificates (P1-041).

## Notes
- Found by P1-039's thorough runs; named as a follow-up in P1-033's and P1-039's Notes.
