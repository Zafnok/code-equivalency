# P2-204 The IL lowering keeps the stack slot of `x as T ?? c` typed `object`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-192's sample `samples/callee-replacement` has `Culture.Current() => X.GetData(Key) as string ?? "invariant"` on
both sides. The compiler leaves the `isinst string` result and the constant in one evaluation-stack slot, and ILSpy's
ILAst types that slot `object`. P2-111 types an `object` slot by the one type stored into it, but this slot keeps
`object`: the IL lowering stores the value through `cast.System.String.System.Object` and reads it back through
`cast.System.Object.System.String`, which the IOperation lowering of the same expression does not use. Z3 cannot
know the two casts cancel, so the two lowerings of this one method are Divergent, and
`IlLoweringParityTests.Known` lists it on both sides with reason `AsThroughObjectSlot`. In an IL-fallback pass
(ADR 0039) such a body would be compared with a representation the other side's lowering does not have.

When done, this slot has type `string` in the IL lowering, like a slot P2-111 already types.

## Spec references
ADR 0039, `docs/tickets/done/P2-111-il-stack-slot-typed-by-its-stores.md`, `docs/tickets/IL-COVERAGE.md` (the `StLoc`
row and the types paragraph), `src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs` (`VariableType`).

## Acceptance criteria (all must hold; nothing beyond them)
1. Find why P2-111's rule does not apply to this slot (which store's type it cannot name), and widen the rule to it
   without typing any slot whose stores have more than one type.
2. `IlLowerer.Lower` of `static string M(object o) => o as string ?? "c";` has no parameter named
   `cast.System.String.System.Object`.
3. `IlLoweringParityTests.IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent` passes with the two
   `callee-replacement` `Current` entries and the `AsThroughObjectSlot` constant deleted.

## Tests
A unit test for criterion 2 in `IlLowererTests`.

## Size guard
A change to how ILSpy is asked for the ILAst: stop, that is not this ticket.

## Out of scope
Slots whose stores have different types.

## Notes
- Filed 2026-10-15 by P2-192 (the IL parity gate on its sample).
