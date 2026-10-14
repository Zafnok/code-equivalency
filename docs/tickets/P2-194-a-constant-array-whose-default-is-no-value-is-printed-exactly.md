# P2-194 A constant array whose default is not a value is printed for cvc5 exactly, wherever it is used
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-033

## Goal
cvc5 does not read a constant array whose default is not a value. On a default such as an
uninterpreted literal constant, it says "expected a value", with or without `--arrays-exp`. ADR
0050 decision 2 rewrites such an array into a fresh array constrained where the query reads it.
`SecondSolver.Print` declines the query whenever the array is used any other way: as an argument
of a function, in an equality that is not its definition, or as an element of another array.

P1-026 measured how often that happens (`docs/runs/2026-10-14-proof-certificate-spike.md`). On
`gitextensions-9860`, the queries of 76 of the 302 solver Equivalents are declined: 71 `bounded`
and 5 `lockstep-induction`, 149 queries in all. Such a query never reaches cvc5, either as the
second solver (ADR 0050) or for a certificate (ADR 0057).

When done, such an array is printed exactly in every use. One rewrite that is exact everywhere: a
fresh array constant `a` and `(assert (forall ((i I)) (= (select a i) d)))`. cvc5 reads
quantifiers, and Alethe proves with them (`forall_inst`). The query is then sent whatever the use.

## Spec references
ADR 0050 decision 2, ADR 0057, `src/Equiv.Verify.Z3/SecondSolver.cs`
(`WithoutSymbolicConstantArrays` and its doc), `tools/spikes/proof-certificates/` (the count).

## Acceptance criteria (all must hold; nothing beyond them)
1. `SecondSolver.Print` prints every query that holds a constant array whose default is not a value.
   No query is declined for it. The printed query is satisfiable exactly when the original is.
   ADR 0050 gets a dated clarification that says how.
2. A property test: on generated terms with such arrays in each use the old rewrite declined, Z3
   gives the printed query and the original the same answer.
3. cvc5 reads every one of the 149 queries P1-026 could not send. P1-026's spike, rerun on
   `gitextensions-9860`, reports how many of the 76 results it now certifies.
4. ADR 0050's second solver sends those queries when Z3 gives up on them. A quick run of
   `gitextensions-8522` with cvc5 configured changes no result that run decided before.

## Files
`src/Equiv.Verify.Z3/SecondSolver.cs`, `docs/adr/0050-cvc5-is-asked-the-rung-1-queries-z3-gives-up-on.md`
(clarification), tests.

## Tests
The property test of criterion 2. A unit test per use the old rewrite declined: a function
argument, an equality that is not the definition, and an element of another array.

## Size guard
A change to the encoding itself, so that it holds no such array, rather than to how it is printed:
stop. That is a different ticket.

## Out of scope
Certificates (P1-041). Other parse errors cvc5 reports.

## Notes
- Found by P1-026.
