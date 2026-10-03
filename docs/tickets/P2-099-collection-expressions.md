# P2-099 A collection expression equals the `new` and initializer it replaces
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
IDE0028 turns `new()`, `new T[] { ... }`, `Array.Empty<T>()` and collection initializers into
`[...]`. A collection expression lowers as an opaque `Conversion` to its target type. On
`gitextensions-11372`, a pull request that does nothing else, `Conversion` is in the reason set of
308 of 351 changed pairs, the solver proved 7 (2.0%), and 2 EQ002 are false: the legacy trace
starts with the list constructor and the modern one with the first element's constructor. Lower a
collection expression so that it is the same value as the construct it replaces, for the target
types the pull request uses: arrays, `List<T>`, and the interfaces they implement.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (`Conversion`, `ArrayCreation`, `ObjectCreation`,
`CollectionExpression` if present); `.claude/skills/equiv-extend-ir/SKILL.md`; P2-071 (effect-free
BCL calls in the trace), which owns whether a `List<T>` constructor is a traced call;
`docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Apply `equiv-adr`'s bar test first: the choice is between lowering to the calls the old form
   makes and a new IR node for a collection value. Record the outcome.
2. Samples, each Equivalent with a `proofMethod` other than `congruence`:
   - `new T[] { a, b }` against `[a, b]` for an array target;
   - `new List<T>()` and `new()` against `[]` for a `List<T>` target;
   - `new List<T> { a, b }` against `[a, b]`;
   - `Array.Empty<T>()` against `[]` for an array or `IEnumerable<T>` target.
3. Element order and element side effects are kept: a sample that swaps two elements with side
   effects is Divergent.
4. A spread element (`..`) and a target type with a `CollectionBuilder` stay opaque, with a named
   reason, unless the decision covers them.
5. Rerun `gitextensions-11372`. Notes record the solver-proved share of changed pairs next to the
   2.0% it has now, and the two EQ002 named in the summary.

## Tests
- `IrLowererTests.ACollectionExpressionForAnArrayLowersAsAnArrayCreation`
- `IrLowererTests.ACollectionExpressionWithASpreadIsOpaque`
- `EquivalenceTests.CollectionExpressionEqualsItsInitializer`
- `EquivalenceTests.SwappedElementsWithSideEffectsDiverge`

## Size guard
More than the four target shapes above: stop, the rest is its own ticket.

## Out of scope
Dictionary targets, spans, `ImmutableArray<T>` and other builder-based targets.

## Notes
