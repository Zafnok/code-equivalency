# P1-006 Array maps are keyed by the array value, not by the variable
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M2-004, M3-001, M3-007, P1-003

Promoted into M3 by ADR 0018: lands before M3-003, despite the P1 id. The new `array.<Elem>`
maps are `Ref` like every `array.*` map (M3-007); `length` stays `In`.

## Goal
`void M(int[] a, int[] b)` stops treating `a` and `b` as disjoint when the caller passes one
array twice. The element and length slices move from one input per array *variable*
(`array.<v>`, `length.<v>`) to one input per element type keyed by the array reference:
`array.<Elem>` of `Map(Sort(System.Array), Map(BitVec(32), Elem))` and `length` of
`Map(Sort(System.Array), BitVec(32))`. Two variables holding the same reference then select
the same slice, which is what aliasing means, and the null shadow already keys off the same
reference so nullness and elements agree.

## Why it is not in M2-004
M2-004 acceptance criterion 6 specified `array.<v>` per variable, and its Notes record the
alternative this ticket now takes ("one map per element type keyed by (array, index), which
models aliasing but is not what the ticket describes"). ADR 0015 accepts the gap as a named
limit and files it here.

## Spec references
VERIFICATION-MODEL.md sections 1, 2 (the synthesised-inputs paragraph and the two-gaps
paragraph), 5, 7; ADR 0015; M2-004 acceptance criterion 6 and its Notes; the
`equiv-extend-ir` skill.

## Design
No IR change: a nested `IrMap` is already expressible and `IrMapRead`/`IrMapWrite` already
compose, so an element read becomes two `IrMapRead`s (slice, then element) and a write
becomes a read of the slice, a `store` into it, and a `store` of the slice back. The whole
change is `HeapInputs`, the lowerer's array path and the input naming in
VERIFICATION-MODEL section 2. The encoder needs nothing new: `ArraySort` of `ArraySort` is
already what `IrMap` maps to.

The key sort is the array reference's own sort. An array's `IrSort` name comes from
`TypeMapper.MetadataName`, so `int[]` and `string[]` are different sorts and no cross-type
key confusion is possible; that also means `length` is one input per array sort, not one
globally. Settle the exact input names with `equiv-decide` and log one `Decision:` line;
whatever they are, they stay dot-separated and inside the IR name grammar (M1-002).

## Acceptance criteria (all must hold; nothing beyond them)
1. `HeapInputs.Elements` and `HeapInputs.Length` take the array's `IrSort` and element type
   instead of a variable name, and return one input per (array sort, element type) pair. No
   input name mentions a local variable. `HeapInputs`'s XML doc and VERIFICATION-MODEL
   section 2's synthesised-inputs paragraph are updated together.
2. An `IArrayElementReferenceOperation` read lowers to a slice `IrMapRead` keyed by the
   array value followed by an element `IrMapRead` keyed by the bv32 index; a write lowers to
   slice read, element `IrMapWrite`, slice `IrMapWrite`. The bounds check reads the length
   map with the same array value as key, and `a.Length` uses that same read.
3. A test named `Aliased_ArrayParameters_AreNotDisjoint` lowers
   `static int M(int[] a, int[] b) { a[0] = 1; b[0] = 2; return a[0]; }` and asserts both
   accesses go through one element map input; the corresponding Z3 fixture
   `array-alias.ir` under `tests/Equiv.Verify.Z3.Tests/Fixtures/` is Divergent against the
   variant that returns `2`, and Equivalent against itself.
4. `LoweringOracleGen` gains the case that catches the gap: a generated method taking two
   `int[]` parameters that writes through one and reads through the other, invoked with the
   same array passed twice. Adding the case on top of `main` before this ticket's fix fails
   the lowering oracle.
5. Every M2-004 array snapshot is re-approved with the new shape, and the diff in each is
   only the extra map level; `Samples_LowerWithoutOpaque` still passes with zero `IrOpaque`.
6. An array that is not a plain variable, a multi-dimensional array and a long index stay
   `IrOpaque` exactly as M2-004 left them; this ticket changes the key, not the coverage.
7. VERIFICATION-MODEL section 2's two-gaps paragraph has its second gap replaced by a
   sentence saying it is closed here. The first gap and its P1-005 reference stay.
8. P1-004's ticket text is updated to the new input names if P1-004 has not yet landed.
9. If P1-005 has already landed, an `IrCall` havocs the new element and length maps on the
   same terms as its `field.*` maps, with an array counterpart to P1-005's
   `call-heap-order.ir` fixture. If it has not, P1-005 picks this up under its own criterion
   3. Once both have landed a call's effect on array elements is modelled; neither ticket
   may leave that open.

## Files
`src/Equiv.Frontend.CSharp/Lowering/HeapInputs.cs` and the array path of the heap
collaborator P1-003 extracts; `tests/Equiv.TestSupport/LoweringOracleGen.cs`;
`docs/VERIFICATION-MODEL.md`; snapshot approvals; `docs/tickets/P1-004-foreach-as-an-index-loop.md`.

## Tests
`Aliased_ArrayParameters_AreNotDisjoint`, the re-approved array snapshots, the
`array-alias.ir` fixture pair, the extended lowering oracle.

## Size guard
No new file in `src/Equiv.Core` and no change to `Equiv.Verify.Z3`. If the encoder needs a
case for nested maps, stop: `IrMap` of `IrMap` is meant to already work, and a gap there is
an M3-001 bug, not this ticket.

## Out of scope
An alias analysis that proves two references distinct (the map key does the work instead).
Arrays of arrays as *elements*. `Span<T>`, `List<T>`, collection expressions.

## Notes
- Decision: the inputs are `array.<Sort>` and `length.<Sort>`, `<Sort>` being the array's own IR sort name spelled with
  `HeapInputs.Part` exactly as `null.<Sort>` already spells it (`int[]` → `array.int__`, `length.int__`, beside
  `null.int__`). Naming by the array sort rather than the element type keeps one name per key sort, so a name can
  never be asked for at two different map types.
- Criterion 4, checked before the fix: with the generator extended (two `int[]` parameters `u`, `v`, element reads as
  `int` leaves, element writes at index 0..2, `v` bound to `u` when `OracleInput.Aliased`) and the harness binding the
  old `array.u`/`array.v`/`length.u`/`length.v` inputs, the oracle failed on its first sample:
  `v[0] = unchecked(P ^ (1)); return ...;` with `Aliased = True` gave C# `u=6,7 v=6,7` and IR `u=31,7 v=6,7`. The
  harness now compares the final elements of both arrays along with `F`, so a write through one alias that the other
  does not see fails the oracle whether or not the method reads it back.
- `Samples_LowerWithoutOpaque` is `SampleLoweringTests.EveryMatchedPairLowersWithoutOpaqueNodes`; no sample indexes an
  array, so it is unaffected.
- Left alone, out of this ticket's files: `IrUnroller.InliningObstacle` still refuses to inline a self-call when any
  `array.*`/`length.*` input exists ("an input is keyed by an array variable", ADR 0015). That was needed while the maps
  were per variable; keyed by value, a read-only array map inlines as soundly as a `field.*` map, and a written one is
  already stopped by "it writes the heap". Still sound, only conservative, and the message is now stale.
- Observed, pre-existing and not this ticket: an element (or field) assignment null-checks the array (receiver) before
  evaluating the right-hand side, but the CLR throws `NullReferenceException` at the `stelem`/`stfld`, after it. So
  `a[0] = F();` with `a == null` calls `F` in C# and not in IR. The oracle never passes a null array, so it cannot see
  this.

