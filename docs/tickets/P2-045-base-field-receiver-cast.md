# P2-045 A field read or written through `base` lowers to IR that fails validation
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-031

## Goal
A Debug build of `equiv compare --execute` on the Tomas corpus pair
(`pmb-tomasjohansson__adapters-shortest-paths-dotnet`) stops in `lower` on `Debug.Assert` "lowered IR must
validate" (`IrLowerer.Procedure`). Release skips the assert and hands the ill-typed IR on. Observed
2026-09-28 while verifying P2-044 (PR #279).

The failing procedures are `edu.ufl.cise.bsmock.graph.util.DijkstraNode::GetParent()` and
`::SetParent(string)`, both `IR009` (types do not fit):
`%$0: sort "edu.ufl.cise.bsmock.graph.Node" = mapread %cast.edu.ufl.cise.bsmock.graph.Node.edu.ufl.cise.bsmock.graph.Node, %this`.
Both access `base.neighbors`, a field declared on the base class `Node`. `base` is lowered to `this`, of the
containing type (`DijkstraNode`, M4-001), but P2-031's receiver upcast in `HeapLowerer.Member` names the cast
map from the receiver operation's static type, which for `base` is the base (`Node`). So it reads
`cast.Node.Node`, a `map<Node, Node>`, at a `DijkstraNode` value.

Minimal repro: `class B { public int f; } class C : B { int N() => base.f; }`.

## Spec references
`docs/tickets/done/P2-031-domain-sort-mismatch-inherited-this.md` (the receiver upcast);
`docs/tickets/done/M4-001-foreach-using-constructors.md` (`base` lowered to the containing type's `this`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A field read or written through `base` keys its map with `this` upcast through `cast.<Derived>.<Base>`,
   as `this.f` does, and the procedure validates.
2. The minimal repro, and a `base` field write, verify Equivalent against themselves.
3. The Debug CLI no longer trips the assert on the Tomas pair.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs`, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`

## Tests
- Unit: `IrLowererTests.AnInheritedFieldIsReadAtTheUpcastReceiver` gains a `base.f` case.
- Integration: `InheritedThisEquivalenceTests.AMemberOfABaseClassReachedFromADerivedOneVerifies` gains a
  `base.X` read and a `base.X` write case.

## Out of scope
`base` as the receiver of a catalogue-adapted call (`IrLowerer.Adapted` also takes the operand's static
type); calls are keyed by identity, not type-checked against the callee, so it does not fail validation.

## Notes
- Decision: `HeapLowerer` gets the type of an operand's lowered value through a new constructor delegate
  (`IrLowerer.TypeOf`: the containing type for a `ContainingTypeInstance` reference, else the operand's
  type), like its other callbacks into `IrLowerer`, rather than changing what `base` lowers to: calls through
  `base` rely on it being `this` of the containing type (`ABaseCallPassesThisOfTheContainingType`).
- Found by temporarily logging `IrValidator.Validate`'s diagnostics and procedure identity at the assert
  and running the Debug CLI on the pair. After the fix, the same run with `--execute` finishes (exit 1:
  Divergent results) with no assert.
