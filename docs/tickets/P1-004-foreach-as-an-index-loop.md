# P1-004 Lower `foreach` over an array as an index loop
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004, M3-011

## Goal
`foreach (T x in a)` where `a` is an array lowers to the index loop the C# compiler emits
(`for (int i = 0; i < a.Length; i++) { T x = a[i]; ... }`) instead of the enumerator calls
M3-011 lowers it to. Those calls are sound but tell the solver nothing about the elements, so
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
   `IrOpaque` nodes: the element read is an `IrMapRead` on that array's map, the bound is the
   array's `length:<arr>` var, and the index is a bv32 SSA variable with a phi at the header.
2. The pattern is recognised from the CFG and the operation tree together, never from syntax,
   and a `foreach` whose shape does not match stays as M3-011 lowers it.
3. `foreach` over anything that is not an array stays as M3-011 lowers it.
4. A snapshot test per shape (array of a bitvector element type, array of a `Sort` element
   type, `foreach` with `break` and with `continue`), an `IOPERATION-COVERAGE.md` row for
   `ForEachLoop`, and a lowering-oracle case that sums an `int[]`.

## Out of scope
`foreach` over `string`, `Span<T>`, a user type with a `GetEnumerator` method, or
`IEnumerable<T>`; collection expressions; `await foreach`.

The element map this ticket reads inherits the M2-004 aliasing limit ADR 0015 names: it is
keyed per array *variable*, so a `foreach` over an array that some other variable also
holds sees its own slice. P1-006 closes that for every array access at once. Do not work
around it here; if P1-006 has landed first, use the input names it introduces (acceptance
criterion 1 above says `length:<arr>`, which M2-004 shipped as `length.<v>` and P1-006
replaces again).

## Notes
