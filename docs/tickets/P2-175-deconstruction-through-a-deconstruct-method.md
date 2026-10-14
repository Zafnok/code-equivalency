# P2-175 A deconstruction through a `Deconstruct` method lowers as the call it is, not as an opaque
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-170

## Goal
P2-170 lowers a deconstruction of a tuple-typed value. Its census of `gitextensions-11284` (equiv
`0a2f9a03`, both sides alike) found 102 opaque deconstructions a side: 80 of a tuple-typed value and
22 through a `Deconstruct` method. P2-170 lowers 78 of the 80. What is left, a side:

| form | sites |
|---|---|
| `Deconstruct` method, as the variable of a `foreach` (`foreach (var (k, v) in dictionary)`) | 21 |
| `Deconstruct` method, as a statement | 1 |
| tuple-typed value into a property target | 2 |

One of the 22 also has a nested tuple target. No deconstruction of the pair has its value used.

In P2-170's quick rerun `DeconstructionAssignment` is still in the reason set of 16 of the 178 changed
pairs, the whole reason set of 5, and the first reason of 9 Unknowns of the review list. The
`Deconstruct` method is the largest form left.

C# binds such a deconstruction to one call of `Deconstruct` with an `out` argument per target
(`KeyValuePair<TKey, TValue>.Deconstruct(out TKey, out TValue)` for a dictionary's element), then
stores each output, converted, to its target. A call with `out` arguments already lowers (M4-003).

When done, a statement or a `foreach` variable that deconstructs through a `Deconstruct` method
into locals, parameters, fields and discards lowers as one evaluation of the value, one call of the
method, and one store per target, and `var (k, v)` and `(K k, V v)` lower to the same IR.

## Spec references
`IOPERATION-COVERAGE.md` rows `DeconstructionAssignment` and `Invocation`; VERIFICATION-MODEL
section 3 (the deconstruction paragraph); `docs/tickets/done/P2-170-*.md`,
`docs/tickets/done/P2-025-deconstruction-assignment.md`, `docs/tickets/done/M4-003-ref-out-call-arguments.md`;
`.claude/skills/equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The method a deconstruction binds to is found without reflection (`IDeconstructionAssignmentOperation`
   does not expose it; `SemanticModel.GetDeconstructionInfo` does). Notes say how, through
   `equiv-decide`.
2. A deconstruction through an instance `Deconstruct` method whose outputs reach their targets
   through identity conversions lowers as the call, with the callee identity a written-out
   `value.Deconstruct(out a, out b)` has. An extension `Deconstruct`, a converting output and a
   nested target stay opaque unless they fall out of the same code; the coverage row says which.
3. A sample pair that changes only `foreach (var (k, v) in d)` to `foreach ((K k, V v) in d)` is
   Equivalent; a sample that swaps the two targets is not Equivalent.
4. Whatever stays opaque still writes its local and parameter targets with unknown values (P2-009).
5. Rerun `gitextensions-11284` in compare mode quick. Notes record the reason-set count next to 16
   and the solver-proved count next to 99 of 178.

## Tests
- `IrLowererTests` and `IrLowererSnapshotTests`: one per new shape, and one for a shape that stays
  opaque.
- `DeconstructionEquivalenceTests`: criterion 3's two samples.
- `LoweringOracleTests` covers the new shape if a `Deconstruct` method fits its generator.

## Size guard
A property or array element target: stop, that is its own ticket (2 sites a side on this pair).

## Out of scope
A deconstruction whose value is used; a positional pattern, which calls `Deconstruct` too but is
`switch-pattern`'s.

## Notes
- Filed 2026-10-13 from P2-170's census and rerun. No open ticket owned the remainder.
