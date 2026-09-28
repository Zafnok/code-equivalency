# P2-033 Verifying crashes with "IrSortValue ... was not present in the dictionary"
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
M4-007's first real run hit the same crash message on two unrelated corpus pairs, deterministically
and reproducibly (same method, every run): on `pmb-tomasjohansson__adapters-shortest-paths-dotnet`,
`Verifying Programmerare.ShortestPaths.Adaptee.YanQi.Test.YenTopKShortestPathsAlgTest::GetExpectedWeightAndNodes(string) against ...(string) failed: The given key 'IrSortValue { Type = IrSort { Name = System.String }, Sort = System.String, Id = 1174359459 }' was not present in the dictionary.`
On Git Extensions, a `KeyNotFoundException` with a different `IrSortValue` also fired (8 occurrences in all). Whatever dictionary is keyed by `IrSortValue`, the lookup at
verification time uses a value that was never inserted (or was inserted under a different, but
`Equals`-equal, instance — check `IrSortValue`'s `Equals`/`GetHashCode` for a case where two
"equal" values hash or compare differently than a `Dictionary` expects, or the id `1174359459` is
itself the clue: it looks like a `RuntimeHelpers.GetHashCode`/object-identity-derived id computed
before some later step that produces a structurally-equal but reference-different `IrSortValue`
for the same source value).

## Spec references
`IrSortValue` (search `src/Equiv.Core`); whatever dictionary in `src/Equiv.Verify.Z3` looks up a
constant/value's encoding by `IrSortValue` during Z3 term construction.

## Acceptance criteria (all must hold; nothing beyond them)
1. Reproduce the crash with a small standalone repro (likely a `string`-typed switch/pattern-match
   test method with generic type arguments in its identity, matching the two real occurrences'
   shape) and add it as a regression test before fixing anything else.
2. Fix the root cause (most likely `IrSortValue`'s equality contract, or a place that constructs a
   second value instead of reusing the interned one) so the repro verifies instead of throwing.
3. Confirm both real crashes (Tomas's `GetExpectedWeightAndNodes`, and Git Extensions' occurrence)
   no longer reproduce.

## Size guard
If this turns out to require redesigning how constants/values are interned across the whole
verification pipeline, stop and write an ADR instead of a narrow equality-contract fix.

## Out of scope
Any other `KeyNotFoundException` not tied to `IrSortValue`.

## Notes
