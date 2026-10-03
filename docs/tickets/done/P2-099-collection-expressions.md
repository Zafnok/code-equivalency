# P2-099 A collection expression equals the `new` and initializer it replaces
Status: done (PR #368)
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
- Bar test (criterion 1): no ADR and no clarification. A collection expression lowers to the calls and
  the allocation the old form makes, with instructions the IR already has, so no component boundary,
  Core contract, verdict or SARIF shape changes; the rule is one paragraph in VERIFICATION-MODEL
  section 3. A new IR node for a collection value lost: it is a Core contract change (a new ADR), and
  it needs a model of what a `List<T>` holds, which P2-071 owns.
- Decision: what `[]` is for an array or read-only interface target -> the call `System.Array::Empty<T>()`, which is what the compiler emits; a zero-length allocation only where the framework has no `Array.Empty`. Alternatives: always a zero-length allocation (then `Array.Empty<T>()` against `[]` differs by a call), lowering `Array.Empty<T>()` itself as an allocation (P2-071's call, and it makes two results distinct references). Rule: 1.
- Decision: interface targets -> `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IReadOnlyList<T>` are the array, read through the `cast` map the old form's conversion reads; `IList<T>` and `ICollection<T>` stay opaque. Alternatives: lower the two mutable ones as a `List<T>`, which the operation does not name (it has no construct method), so it needs a type lookup that can fail. Rule: 4.
- Decision: which class targets -> `List<T>` only, by its parameterless constructor and its one `Add`; every other collection-initializer type, a `CollectionBuilder` type and a span stay opaque. Alternatives: every type with a constructor and an `Add` (the operation does not say which `Add` binds). Rule: 4 (and the size guard).
- Decision: nullness -> a collection expression is known non-null exactly where the old form is (an array creation, a `new List<T>`); an `Array.Empty` call's result and a value read through a cast map ask `null.<Sort>`, as they do on the old side. Alternatives: always non-null (true at run time, but then the two sides differ on a null check the old side models). Rule: 1.
- Decision: opaque reason -> `CollectionExpression`, the operation's kind, on the collection expression and not on its conversion. Alternatives: keep `Conversion`. Rule: 3.
- Deviation: the goal says "the interfaces they implement"; `IList<T>` and `ICollection<T>` stay opaque (decision above), and so does a class other than `List<T>`. Both are P2-109.
- Surprise: a target-typed `new()` was itself an opaque `Conversion`, so `new()` against `[]` (criterion 2) could not be proved whatever `[]` lowered to. The conversion around one whose creation is already of the target type is now its operand; one to a `Nullable<T>` creates a `T` and stays opaque. On gitextensions-11372 that is 1501 of the legacy side's 2151 `Conversion` opaques.
- Rerun (criterion 5), equiv 465133d, 2026-10-03, `docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md`: the solver proves 160 of the 351 changed pairs, 45.6% next to 2.0% (`bounded` 116, `lockstep-induction` 44). Divergent 28 (was 25), Unknown 163 (was 319). `CollectionExpression` is in the reason set of 104 changed pairs; `Conversion` was in 308.
- The two EQ002 the summary names, `RepositoryXmlSerialiserTests::Serialize_recent_repositories()` and `RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`, are still EQ002. Their elements have object initializers, so the control flow graph evaluates every element into a flow capture ahead of the collection expression: the modern trace is the elements, then the list's constructor and its `Add` calls, and the legacy one is the constructor, then each element and its `Add`. No acceptance criterion covers that shape. Both also carry P2-098's caller-file-path cause. Filed as P2-109, with a third pair of the same kind.
- The 3 new EQ002 were Unknown before and lower fully now: 2 are P2-098's caller file path, 1 is P2-109's.
- Toolchain: the corpus skill's `MSBuild -t:restore` for a legacy side fails with MSB4018 on this SDK-style net8.0 solution; `dotnet restore --force` restores it.
- Decision: criterion 2's samples -> snippet pairs in `Equiv.Tests.Integration.CollectionExpressionEquivalenceTests`, as P2-088's are. Alternatives: a `samples/` project pair. Rule: 4.
- Merge with P2-071 (ADR 0043), 2026-10-03: `new List<T>()` is no longer a call, so a `List<T>` collection expression is the next new list and its `Add` calls, with no constructor call. The rerun above predates this; P2-109 and P2-110 rerun on the merged rule.
