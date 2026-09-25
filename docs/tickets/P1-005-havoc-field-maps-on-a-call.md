# P1-005 An `IrCall` reads and writes the heap
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M2-004, M3-001, M3-007, P1-003

Promoted into M3 by ADR 0018: lands before M3-003, despite the P1 id.

## Goal
`Foo(o); x = o.F;` and `x = o.F; Foo(o);` stop encoding identically. An `IrCall` names the
`field.*` maps its callee could write and produces a fresh SSA version of each, defined by
an uninterpreted function of the callee identity, the arguments and the incoming map. Both
sides of a pair share that function, so a call that is the same on both sides still has the
same heap effect on both sides and an unchanged procedure is still Equivalent.

ADR 0018 widens the goal. The heap is an *input* to a call as well as an output:
`x = 1; Save(this); x = 0;` and `x = 2; Save(this); x = 0;` must be Divergent, because `Save`
may read `x`. So the heap at the call is part of the call's trace event, and it is an argument
of every function the call is encoded with. Those functions are also keyed by the call's
position, as M3-001 already does for results.

## Why it is not in M2-004
M2-004 acceptance criterion 6 asked for one map per field and nothing about calls. Havocking
on a call needs a rule for *which* fields a callee can touch, a new output list on `IrCall`,
and an encoder case, which is three projects and past that ticket's size guard. ADR 0015
accepts the gap as a named limit and files it here.

## Spec references
VERIFICATION-MODEL.md sections 1, 2 (the two-gaps paragraph), 5, 7; ADR 0015; ADR 0014;
the `equiv-extend-ir` skill, whose four-step order this ticket follows exactly.

## Design
The effect is a *function*, not a havoc to an unconstrained value: an unconstrained fresh map
on each side makes every procedure containing a call Divergent, which is the mirror of the
bug being fixed. Let H be the heap at the call: the current version, on that side, of every
`field.*`/`array.*` map named by *either* side of the pair, ordered by name. Per callee
identity and per map, the encoder declares one `FuncDecl`
`f_callee$heap$<map>(args..., pos, H...) -> map`, shared by both sides exactly as
`f_callee` already is (M3-001), and side-specific for an identity in the runtime-changes
table (M2-006). `f_callee` and `f_callee_threw` gain the same `H...` arguments, and the
call's trace event gains H, so heaps that differ at a call are a trace difference.

The frontend only knows its own side, so it emits a (before, after) pair for each map *it*
names. For a map only the other side names, the encoder threads a version chain on this side
itself: the input, then each call's `f_callee$heap$<map>` result in execution order. That
chain is what makes H line up across sides. `IrCall` carries the writes so that Core's validator, dump format and
interpreter agree with the encoder; the interpreter's call oracle returns the new maps
alongside the value it already returns.

Which fields a callee could reach is the open question, and the conservative answer is the
only sound one available without a call graph: every `field.*` map the *caller's* body
mentions. A procedure that never touches a field is unaffected; one that does pays a fresh
version per call. Narrowing it to the fields a known callee actually writes needs a whole-
program analysis and is explicitly out of scope.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrCall` gains a list of (before, after) SSA pairs, one per heap map the call reads and
   writes (name settled by `equiv-decide`; one `Decision:` line). `before` is a use and
   `after` a definition. The validator rejects an `IrCall` whose heap pairs repeat a map, name
   a non-`IrMap` var, or re-use an SSA name; `IrText` round-trips the new form; the M1-002
   dump/parse property generator is extended, not duplicated.
2. `IrInterpreter` applies the new versions: the call oracle returns a map value per
   heap pair, and the default oracle leaves each map unchanged so existing
   interpreter tests keep their current results.
3. `IrLowerer` emits, on every `IrCall` it lowers, one heap pair per `field.*` map the
   procedure touches *anywhere* in its body, not only the maps created by the time the call is
   lowered. `Foo(); x = o.F;` must read `F`'s `after` version even though `F` is first touched
   after the call, so collect the set before lowering, or fix up after it. There is none for
   `null.*` or `this`
   (a reference's nullness is a property of the value, which a call cannot change). A
   procedure with no field access emits an empty list and its IR dump is byte-identical to
   the one M2-004 produced.
   Array slices: P1-006 has landed, so the `array.*` maps are keyed by the array value and
   are included on the same terms as `field.*` maps (`length.*` is not: a call cannot change
   an array's length), and the criterion 6 fixtures gain an array counterpart to
   `call-heap-order.ir`. P1-005 lands second, so it owns this; it may not leave a call's
   effect on array elements unmodelled.
4. `ProductEncoder` encodes each `after` as `store`-free equality to
   `f_callee$heap$<map>(args..., pos, H...)`, one `FuncDecl` per (callee identity, map
   name) pair shared across sides, side-specific when the identity is `RuntimeChanged`.
   `f_callee`, `f_callee_threw` and the trace event take H as in the Design section, and
   maps named by one side only are threaded by the encoder on the other side.
5. `LoweringOracleGen` gains the case that catches the gap: a generated method with an
   instance field written and read around a call to a method that writes that field. Adding
   the case on top of `main` before this ticket's fix fails the lowering oracle; a test
   named `Call_HavocsFieldsWrittenByCallee` pins the minimal shape independently of the
   generator.
6. Four fixtures under `tests/Equiv.Verify.Z3.Tests/Fixtures/`:
   - `call-heap-order.ir` (a pair differing only in whether the field read precedes or
     follows the call) is Divergent;
   - `call-heap-same.ir` (the same call and the same order on both sides) is Equivalent;
   - `call-reads-heap.ir` (`x = 1; Save(this); x = 0;` vs `x = 2; Save(this); x = 0;`) is
     Divergent, although final heaps and results agree;
   - `call-heap-one-sided.ir` (only old names `field.C.f`, reading it after a call both sides
     make and writing the read value back) is Equivalent, through the encoder's threading.
7. A snapshot test per shape (a call with one field map live, a call with two, a call with
   none) and an `IOPERATION-COVERAGE.md` update for `Invocation` naming this ticket.
8. VERIFICATION-MODEL section 2's `IrCall` row states the heap effect, and the two-gaps
   paragraph's first gap is replaced by a sentence saying it is closed here. Section 2's
   sentence on the second gap, closed by P1-006, stays.

## Files
`src/Equiv.Core/Ir/IrCall.cs`, the validator, `IrText`, `IrInterpreter`;
`src/Equiv.Frontend.CSharp/Lowering/` (the heap collaborator P1-003 extracts);
`src/Equiv.Verify.Z3/ProductEncoder.cs`; `tests/Equiv.TestSupport/LoweringOracleGen.cs`;
`docs/VERIFICATION-MODEL.md`; `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
`IrCallValidator_*` per validator rule, the extended round-trip property,
`Call_HavocsFieldsWrittenByCallee`, the two Z3 fixtures, three lowering snapshots, the
extended lowering oracle.

## Size guard
One new IR field, one encoder case (plus the one-sided threading), one lowering change. If `Equiv.Core` grows a new
instruction record, or the fix needs a call graph, stop and write an ADR.

## Out of scope
Narrowing the havoc set per callee (needs a call graph). `array.*` aliasing (P1-006).
Modelling `ref`/`out` arguments' effect on the heap beyond what M2-004 already does.
Purity attributes or a user-supplied "this callee is pure" config.

## Notes
