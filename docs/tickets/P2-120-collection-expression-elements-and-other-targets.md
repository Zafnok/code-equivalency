# P2-120 A collection expression whose elements are evaluated ahead of it, and the targets P2-099 left opaque
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-099, P2-071

## Goal
P2-099's rerun of `gitextensions-11372` (`docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md`)
proves 160 of 351 changed pairs, and leaves two things.

**Element order, 3 false EQ002.** When an element needs more than one operation, such as
`new T { P = x }` or `a ?? b`, the control flow graph evaluates every element up to it into flow
captures before the collection expression. The modern trace is then every element, the list's
constructor, every `Add`. The legacy collection initializer is the constructor, then each element
followed by its `Add`. Both build the same list; the model sees two call orders. The pairs:
`GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Serialize_recent_repositories()`,
`GitCommandsTests.UserRepositoryHistory.Legacy.RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`
and `GitCommandsTests.ExternalLinks.ExternalLinkRevisionParserTests::GetDefaultRemotes()`.

**Opaque targets, 104 changed pairs.** `CollectionExpression` is in the reason set of 104 changed
pairs, 68 of them Unknown for it alone. P2-099 lowers arrays, `List<T>` and the three read-only
interfaces. A spread, `IList<T>`, `ICollection<T>`, a class with a constructor and an `Add` other
than `List<T>` (the third pair above builds a `BindingList<T>`), a span and a `CollectionBuilder`
type stay opaque.

## Spec references
VERIFICATION-MODEL section 3 (the collection expression paragraph); P2-071 and its ADR, which decide
whether a collection's constructor is a trace event at all; ADR 0018; `equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over the modern side of `gitextensions-11372`, how many opaque collection
   expressions each cause accounts for (spread, each target kind). Record the table in Notes and
   take the causes in that order.
2. A `List<T>` collection expression whose elements are flow captures is Equivalent to the
   collection initializer with the same elements, by a `proofMethod` other than `congruence`: a
   sample with an object-initializer element and one with a `??` element. Where the first element's
   evaluation starts at its capture, the constructor call goes there and each `Add` follows its
   element; decide through `equiv-adr` whether that, or P2-071's rule taking the calls out of the
   trace, is the vehicle.
3. `IList<T>` and `ICollection<T>` targets lower as the `List<T>` the compiler builds, and a class
   with a parameterless constructor and one applicable `Add` lowers as its collection initializer.
4. Swapped elements with side effects stay Divergent in every new shape.
5. Rerun `gitextensions-11372`. Notes record the solver-proved share next to 45.6%, the three pairs
   above, and the `CollectionExpression` reason-set count next to 104.

## Tests
- `CollectionExpressionLoweringTests`: one per new shape.
- `CollectionExpressionEquivalenceTests`: the samples of criteria 2 to 4.

## Size guard
A spread element is its own ticket if criterion 1 shows it is more than a third of the opaques.

## Out of scope
Dictionary targets, spans, `ImmutableArray<T>` and other builder-based targets.

## Notes
- Found by P2-099's rerun, 2026-10-03.
