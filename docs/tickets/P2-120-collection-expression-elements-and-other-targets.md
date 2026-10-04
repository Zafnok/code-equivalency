# P2-120 A collection expression whose elements are evaluated ahead of it, and the targets P2-099 left opaque
Status: in-progress
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
Dictionary targets with an element, spans, `ImmutableArray<T>` and other builder-based targets.

## Notes
- Found by P2-099's rerun, 2026-10-03.
- Count (criterion 1), `--lower-only` on `main` at 2c2f682, 2026-10-03, opaque collection expressions on the modern side by cause, 110 in all:

  | cause | opaque | after this ticket |
  |---|---|---|
  | `Dictionary<K, V>` | 55 | lowered: every one is `[]` |
  | `HashSet<T>` | 17 | lowered |
  | `GitExtUtils.ArgumentBuilder` | 15 | lowered where its elements go through its one `Add` |
  | `BindingList<T>` | 7 | lowered |
  | spread element | 5 | opaque (P2-125) |
  | `SerializableDictionary<K, V>` 2, `StringCollection` 2, `XmlSerializableDictionary<K, V>`, `SortableBranchesList`, `SortableObjectsList`, `JArray`, `SortedDictionary<K, V>`, `SortedList<K, V>`, `ConditionalWeakTable<K, V>` 1 each | 11 | lowered |
  | `IList<T>`, `ICollection<T>`, a span, a `CollectionBuilder` type | 0 | |

  Every cause but the spread is a class with a parameterless constructor. A spread is 5 of 110, under the size guard's third.
- Bar test (criterion 2, through `equiv-adr`): no ADR and no clarification. Section 3 already says each element is evaluated, then added, before the next; the lowering did not do that where the CFG captures the elements. The vehicle is the ticket's first one: the object is made where the first captured element starts and each `Add` follows its element. The paragraph in VERIFICATION-MODEL section 3 says so. Taking `Add` out of the trace under P2-071's rule lost: `Add` has an effect, so it would amend ADR 0043 and need a model of what a list holds.
- Decision: where a captured element starts -> the first statement or branch value of the graph, in block order, whose syntax lies inside the element's syntax (`SpilledCollections`). Alternatives: follow each capture id back to its writes (a `??` writes one capture in two blocks, and an object initializer writes members after the capture). Rule: 4.
- Decision: where the object is held between its creation and the collection expression -> an SSA variable of its own, `$collection<n>`, as a flow capture is. Alternatives: the `IrVar` itself (not valid across the copies of a `finally`). Rule: 1.
- Decision: which `Add` a class adds with -> the only method named `Add` with one parameter that the class or a base type declares, when every element has its parameter's type; anything else stays opaque. Alternatives: bind each element as the compiler does (the operation does not expose the binding). Rule: 4. `List<T>` is no longer a special case.
- Decision: the `Add` call -> lowered through the forwarder resolution an invocation gets (`Bound`, ADR 0047), with the `Add`'s own return type. Alternatives: the bare call P2-099 made, which differs from the initializer's for a forwarding `Add` and for one that returns a value (`HashSet<T>`). Rule: 1.
- Deviation: Out of scope names "Dictionary targets", and criterion 3 asks for one applicable `Add`. An empty `[]` for any class with a parameterless constructor lowers as `new T()`, a `Dictionary<K, V>` included: with no element no `Add` binds, and the count puts that cause first with 55 of 110. A dictionary with an element is not lowered. The Out of scope line now says "Dictionary targets with an element".
- Deviation: a struct, and a class whose constructor takes an optional argument, stay opaque. Criterion 3 says "a class with a parameterless constructor".
- Surprise: the count found no `IList<T>` or `ICollection<T>` target at all. They are lowered (criterion 3) and tested, and change nothing on this pair.
- Rerun (criterion 5), equiv 259d051, 2026-10-03, `docs/runs/2026-10-03-cleanup-gitextensions-11372-p2-120/SUMMARY.md`: the solver proves 240 of 319 changed pairs, 75.2% next to 45.6% (`bounded` 170, `lockstep-induction` 70). Divergent 1 (was 28), Unknown 78 (was 163). `CollectionExpression` is in the reason set of 11 changed pairs next to 104. The changed pairs are 319 and not 351 because `main` moved between the two runs (ADR 0043, ADR 0046): the count above, on `main` before this change, already had 14240 congruent pairs. So the 75.2% is not this ticket's alone.
- The three pairs: `RepositoryXmlSerialiserTests::Serialize_recent_repositories()`, `RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()` and `ExternalLinkRevisionParserTests::GetDefaultRemotes()` are each EQ001 by `bounded`. The one EQ002 left is `BugReporter.Program::Main()`, the commit constant.
- What is left, 11 changed pairs: spreads, and classes whose elements are not added through one `Add`. Filed as P2-125.
- The run exits 5 on P2-105's lowering crash, as before.
