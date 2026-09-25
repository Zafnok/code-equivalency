# P2-001 `new T[n]` and array initialisers are lowered, not opaque
Status: done (PR #192)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-006

## Goal
M3-022's census found `ArrayCreation` in the top fifteen opaque reasons of every agent pair (10
bodies over three pairs), and no ticket owned it. Any method that allocates an array is Unknown.
Minimal repro:

```csharp
static int[] Pair(int a, int b) { var r = new int[2]; r[0] = a; r[1] = b; return r; }
static int First(int n) => new[] { n, n + 1 }[0];
```

After this ticket, `IArrayCreationOperation` with one dimension and an `int` length lowers to a
fresh array value: a `System.OverflowException` edge when the length is negative, `length` set to
the length, and every element the default of the element type, or the initialiser's values in order.

## Spec references
VERIFICATION-MODEL section 2 (heap maps); ADR 0015; P1-006 (array maps keyed by the array value);
`docs/tickets/IOPERATION-COVERAGE.md` row `ArrayCreation`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. The two repro methods lower with no `IrOpaque`, snapshot-tested.
2. A negative length branches to `IrThrow("System.OverflowException")`, unit-tested.
3. A multi-dimensional or jagged creation stays `IrOpaque("ArrayCreation")`.
4. The coverage-table row names the tests.

## Size guard
If this needs a new IR instruction, stop and route it through `equiv-adr`.

## Out of scope
Multi-dimensional arrays; `stackalloc`; collection expressions.

## Notes
- Decision: the fresh reference is `new.<Sort>` (an `In` input, bv32 to the array sort) read at a per-sort
  allocation count the body keeps in SSA (`$new.<Sort>`, 0 at entry, +1 per creation), not a sort literal or a
  call: a literal would make every pass of a loop allocate the same array, and a call would put allocation in
  the trace, which section 1 says is not observed. Shared by name, so both sides' k-th creations agree.
  Nothing keeps a fresh array apart from the input arrays; that only adds model states, never drops a real run.
- Decision: `length.<Sort>` is written at the new reference and becomes `Ref` only in a body that creates an
  array of that sort, so every other body's IR is unchanged. Recorded as an ADR 0018 clarification (its
  "`length.*` stays `In`" rested on nothing writing length). Every length read now loads the versioned map,
  which renumbers temps in `IrLowererSnapshotTests.ArrayElements` and changes nothing else in it.
- Decision: `default(T)` is `TypeMapper.Default`: null (sort element 0) for a reference or nullable type,
  `false`, and `Constant(T, 0)` for numeric and enum types (the element a literal `0`/`0.0`/enum zero member
  already is). A struct element type has no constant default and stays `IrOpaque("ArrayCreation")`.
- Decision: an initialiser's values are evaluated and stored one at a time (no null or bounds check), as the
  IL does, so a throwing element leaves the earlier ones in the final heap. A constant length gets no
  negative-length test: the compiler rejects a negative constant.
- Decision: `new[] { ... }[i]` indexes the creation directly, so `HeapLowerer.Element` accepts an
  `IArrayCreationOperation` as the array as well as a plain variable (needed for the second repro).
- Note: a `params` array argument was an `ArrayCreation` opaque and now lowers too; its contents are in the
  `Ref` `array.<Sort>` map, so a changed argument is a changed final heap, not a false Equivalent.
- Note: the session's harness fixes the branch name (`claude/bold-fermat-wk7r7q`), not `P2-001-array-creation`.
- Note: `Equiv.Verify.Z3.Tests` and `Equiv.Tests.Integration` do not restore in this container (the local
  `.z3-feed` source does not exist); only `Equiv.Frontend.CSharp.Tests` ran locally (567 passed). CI runs the rest.

