# P1-005 An `IrCall` havocs the field maps its callee could reach
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M2-004, M3-001, P1-003

## Goal
`Foo(o); x = o.F;` and `x = o.F; Foo(o);` stop encoding identically. An `IrCall` names the
`field.*` maps its callee could write and produces a fresh SSA version of each, defined by
an uninterpreted function of the callee identity, the arguments and the incoming map. Both
sides of a pair share that function, so a call that is the same on both sides still has the
same heap effect on both sides and an unchanged procedure is still Equivalent.

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
bug being fixed. Per callee identity and per field map, the encoder declares one `FuncDecl`
`f_callee$heap$<field>(map, args...) -> map`, shared by both sides exactly as
`f_callee` already is (M3-001), and side-specific for an identity in the runtime-changes
table (M2-006). `IrCall` carries the writes so that Core's validator, dump format and
interpreter agree with the encoder; the interpreter's call oracle returns the new maps
alongside the value it already returns.

Which fields a callee could reach is the open question, and the conservative answer is the
only sound one available without a call graph: every `field.*` map the *caller's* body
mentions. A procedure that never touches a field is unaffected; one that does pays a fresh
version per call. Narrowing it to the fields a known callee actually writes needs a whole-
program analysis and is explicitly out of scope.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrCall` gains `ImmutableArray<IrVar> HeapWrites` (name settled by `equiv-decide` if a
   better one is found; one `Decision:` line if so) holding the post-call SSA version of
   each havocked map, and the pre-call version of each is already in `Args` or in a new
   parallel list. The validator rejects an `IrCall` whose `HeapWrites` repeat a target, name
   a non-`IrMap` var, or re-use an SSA name; `IrText` round-trips the new form; the M1-002
   dump/parse property generator is extended, not duplicated.
2. `IrInterpreter` applies the new versions: the call oracle returns a map value per
   `HeapWrites` entry, and the default oracle leaves each map unchanged so existing
   interpreter tests keep their current results.
3. `IrLowerer` emits, on every `IrCall` it lowers, one `HeapWrites` entry per `field.*` map
   the procedure's `HeapInputs` has created at that point, and none for `null.*` or `this`
   (a reference's nullness is a property of the value, which a call cannot change). A
   procedure with no field access emits an empty list and its IR dump is byte-identical to
   the one M2-004 produced.
   Array slices: while they are keyed per *variable* (M2-004), havocking them is not
   meaningful and they are excluded. If P1-006 has already landed and they are keyed by the
   array value, they are included on the same terms as `field.*` maps, and the criterion 6
   fixtures gain an array counterpart. Whichever of P1-005 and P1-006 lands second owns
   this; neither may leave a call's effect on array elements unmodelled once both have
   landed.
4. `ProductEncoder` encodes each entry as `store`-free equality to
   `f_callee$heap$<field>(incoming, args...)`, one `FuncDecl` per (callee identity, map
   name) pair shared across sides, side-specific when the identity is `RuntimeChanged`.
5. `LoweringOracleGen` gains the case that catches the gap: a generated method with an
   instance field written and read around a call to a method that writes that field. Adding
   the case on top of `main` before this ticket's fix fails the lowering oracle; a test
   named `Call_HavocsFieldsWrittenByCallee` pins the minimal shape independently of the
   generator.
6. Two fixtures under `tests/Equiv.Verify.Z3.Tests/Fixtures/`: `call-heap-order.ir` (a pair
   differing only in whether the field read precedes or follows the call) is Divergent, and
   `call-heap-same.ir` (the same call and the same order on both sides) is Equivalent.
7. A snapshot test per shape (a call with one field map live, a call with two, a call with
   none) and an `IOPERATION-COVERAGE.md` update for `Invocation` naming this ticket.
8. VERIFICATION-MODEL section 2's `IrCall` row states the heap effect, and the two-gaps
   paragraph's first gap is replaced by a sentence saying it is closed here. Section 2's
   second gap and its P1-006 reference stay.

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
One new IR field, one encoder case, one lowering change. If `Equiv.Core` grows a new
instruction record, or the fix needs a call graph, stop and write an ADR.

## Out of scope
Narrowing the havoc set per callee (needs a call graph). `array.*` aliasing (P1-006).
Modelling `ref`/`out` arguments' effect on the heap beyond what M2-004 already does.
Purity attributes or a user-supplied "this callee is pure" config.

## Notes
