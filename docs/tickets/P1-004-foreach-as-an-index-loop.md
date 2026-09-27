# P1-004 Lower `foreach` over an array as an index loop
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004, M4-001

## Goal
`foreach (T x in a)` where `a` is an array lowers to the index loop the C# compiler emits
(`for (int i = 0; i < a.Length; i++) { T x = a[i]; ... }`) instead of the enumerator calls
M4-001 lowers it to. Those calls are sound but tell the solver nothing about the elements, so
an array `foreach` that is unchanged is provable but one that is rewritten into a `for` is not.

## Why it is not in M2-004
M2-004 acceptance criterion 1 asked for it, but Roslyn's `ControlFlowGraph` desugars every
`foreach` -- arrays included -- into the enumerator pattern:
`IEnumerable.GetEnumerator()`, a `Try`/`Finally` region around `MoveNext()`, and
`IEnumerator.Current` as an `IPropertyReferenceOperation` on an uninterpreted receiver. The
CFG never shows an index. Recognising that six-block shape and rewriting it into an index
loop over the `length:<arr>` var and the element map of M2-004 acceptance criterion 6 is
well past the M2-004 size guard (about 150 lines per construct), so the ticket's size guard
sent it here. See the M2-004 Notes.

## Spec references
VERIFICATION-MODEL.md section 3; the `equiv-extend-ir` skill; M2-004 acceptance criteria 1 and 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `foreach` whose `IForEachLoopOperation.Collection` has an array type lowers with zero
   `IrOpaque` nodes: the element read is the array's slice of its sort's `array.<Sort>` map read
   at the array reference, then an `IrMapRead` of that slice at the index (P1-006), the bound is
   `length.<Sort>` read at the same reference, and the index is a bv32 SSA variable with a phi at
   the header.
2. The pattern is recognised from the CFG and the operation tree together, never from syntax,
   and a `foreach` whose shape does not match stays as M4-001 lowers it.
3. `foreach` over anything that is not an array stays as M4-001 lowers it.
4. A snapshot test per shape (array of a bitvector element type, array of a `Sort` element
   type, `foreach` with `break` and with `continue`), an `IOPERATION-COVERAGE.md` row for
   `ForEachLoop`, and a lowering-oracle case that sums an `int[]`.

## Out of scope
`foreach` over `string`, `Span<T>`, a user type with a `GetEnumerator` method, or
`IEnumerable<T>`; collection expressions; `await foreach`.

The element and length maps this ticket reads are the ones P1-006 introduced: keyed by the
array reference, so a `foreach` over an array that some other variable also holds sees the
same elements. Build the accesses through the heap collaborator's array path rather than
naming the maps here.

## Notes
- Decision: where the index steps -> right after the element read (`Current`), so the header's phi takes 0 on entry and
  the stepped index on every back edge, the same values as the compiler's `for` latch. Alternatives: an add on every edge
  back into the header; an index starting at -1 stepped by `MoveNext`. Rule: 4.
- Decision: the array's SSA variable -> the enumerator's own flow capture holds the array and its null shadow, so the
  bound's and the element read's null checks read that shadow. Alternatives: a new synthesised variable. Rule: 4.
- Decision: the index is `$index<n>`, one per recognised loop in the graph. Alternatives: `$i<n>`. Rule: 5.
- Decision: the enumerator's `finally` -> not run: `ExceptionLowerer.Unwind` skips a recognised loop's `finally` region, since
  the compiler's index loop has no `try`. Alternatives: lower its `as IDisposable` as `null`, so the copied `finally`'s null
  test always skips `Dispose`. Rule: 1.
- Decision: the element read -> the outermost operation of the element type over `Current` (the CFG's unboxing or downcast
  from `object`, or `Current` for an `object[]`); a conversion on to the loop variable's type is lowered as it stands, so
  `foreach (long x in int[])` and `foreach (object o in int[])` are index loops too. Alternatives: only a loop variable of
  the element type. Rule: 1.
- Decision: the parts of the shape Roslyn always gives an array loop (the `GetEnumerator` capture of a conversion of the
  array, the `MoveNext` block in the `try`, the `finally` as its sibling) are read by cast, not tested: a test that cannot
  fail is an uncovered branch under the 100% gate. Recognition tests only what the input decides (collection type and
  rank, a declared loop variable, an element-typed operation over `Current`). Alternatives: pattern-test each part.
  Rule: 3.
- Decision: the lowering oracle's array case -> the existing `ForEach` statement enumerates `l`, `u` or `v`
  (`foreach (int w in u) { x op= w; Body }`), so it sums an `int[]` with every arithmetic operator, over aliased arrays,
  with body writes to the array being enumerated, and over a null `v` (the loop throws `NullReferenceException` at its
  first bound). Alternatives: a separate sum-only statement. Rule: 4.
- The element read keeps `a[i]`'s bounds check, which never fires here; it is what a `for` over the same array lowers
  to, so the two shapes stay alike.
- A deconstructing loop (`foreach (var (p, q) in pairs)`) and a multi-dimensional array keep M4-001's non-generic
  enumerator, with its `Conversion` opaques (`IrLowererTests.ForEachOverAnythingElseStaysEnumeratorCalls`).
