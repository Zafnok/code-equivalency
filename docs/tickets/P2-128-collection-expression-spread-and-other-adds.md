# P2-128 A collection expression with a spread, or for a class that does not add through one `Add`
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-120

## Goal
After P2-120, `CollectionExpression` is still in the reason set of 11 of `gitextensions-11372`'s 319
changed pairs (`docs/runs/2026-10-03-cleanup-gitextensions-11372-p2-120/SUMMARY.md`), 9 of them in
the tests of `GitExtUtils.ArgumentBuilder` and of `LazyStringSplit`. P2-120's count on the modern
side had 5 opaque collection expressions with a spread element and 15 for
`GitExtUtils.ArgumentBuilder`. What P2-120 leaves opaque is a spread element, and a class with an
element whose `Add` is not the only one-parameter `Add` the class or a base type declares, taking
the elements' type: two overloads, or an extension method.

## Spec references
VERIFICATION-MODEL section 3 (the collection expression paragraph); `IOPERATION-COVERAGE.md` rows
`CollectionExpression` and `Spread`; P2-120's Notes.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over the modern side of `gitextensions-11372`, how many opaque collection
   expressions each cause accounts for (spread by target kind; class by why no `Add` was found).
   Record the table in Notes.
2. The largest cause lowers as the construct the old form is, and a sample pair of it is
   Equivalent by a `proofMethod` other than `congruence`.
3. Swapped elements with side effects stay Divergent in the new shape.
4. Rerun `gitextensions-11372`. Notes record the `CollectionExpression` reason-set count next to 11.

## Tests
- `CollectionExpressionLoweringTests`: one per new shape.
- `CollectionExpressionEquivalenceTests`: the samples of criteria 2 and 3.

## Out of scope
Spans, `ImmutableArray<T>` and other builder-based targets; key-value elements.

## Notes
- Found by P2-120's rerun, 2026-10-03.
