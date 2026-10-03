# P2-111 The IL lowering types a stack slot by the values stored into it
Status: done (PR #364)
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-068

## Goal
P2-068's sample `samples/forwarder-to-bcl` has `Text.BlankTrimmed(string s) => string.IsNullOrWhiteSpace(s?.Trim())`.
The compiler emits both branches of `s?.Trim()` leaving their value on the evaluation stack, and
ILSpy's ILAst reads that as a stack slot of type `object`. The IL lowering (ADR 0039) takes the
slot's type as ILSpy gives it, so it stores the value through `cast.System.String.System.Object`,
reads its nullness from `null.System.Object`, and loads it back through
`cast.System.Object.System.String`. The IOperation lowering of the same expression uses none of
those three maps, so Z3 finds the two lowerings of this one method Divergent. Type such a slot by
the common type of the values stored into it (here `string`), so the IL lowering emits no `object`
cast and no `null.System.Object` read, and delete the two known-difference entries that record it.

## Spec references
ADR 0039 (the IL lowering produces the IR the IOperation lowering does), `docs/VERIFICATION-MODEL.md`
section 3.1 (`cast.<From>.<To>` and `null.<T>` are identical between the two lowerings),
`docs/tickets/IL-COVERAGE.md` (the `StLoc` and `LdLoc` rows, and the paragraph on types),
`src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs` (`VariableType`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A stack slot whose ILSpy type is `object`, and whose stores' values all have one type the
   lowering can name without lowering them, has that type in the IL lowering. A `null` stored into it
   has no type of its own and does not count. Any other slot keeps ILSpy's type.
2. `IlLowerer.Lower` of `static bool M(string s) => string.IsNullOrWhiteSpace(s?.Trim());` has no
   parameter named `cast.System.Object.System.String`, `cast.System.String.System.Object` or
   `null.System.Object`.
3. `IlLowererTests.CallIdentitiesMatchTheOperationLowering` passes with the `KnownCalleeDifferences`
   entry `forwarder-to-bcl/legacy Equiv.Samples.ForwarderToBcl.Text::BlankTrimmed(string)` deleted.
4. `IlLoweringParityTests.IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent` passes with the
   `Known` entry of reason `NullConditionalOnAStackSlot` deleted, and that constant with it.
5. `docs/tickets/IL-COVERAGE.md` says how a stack slot is typed (the `StLoc` row or the types paragraph).
6. No file under `samples/` changes, and no `*.verified.*` snapshot changes.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs`,
`tests/Equiv.Frontend.CSharp.Tests/Lowering/Il/IlLowererTests.cs`,
`tests/Equiv.Tests.Integration/IlLoweringParityTests.cs`, `docs/tickets/IL-COVERAGE.md`.

## Tests
`IlLowererTests.AStackSlotIsOfTheTypeStoredIntoIt` (criteria 1 and 2: the null-conditional argument,
a slot whose stores disagree, and one whose only store has no type the lowering names), and the two
tests of criteria 3 and 4 with their entries removed.

## Size guard
More than `VariableType` and what it calls changing in `src/` means the ticket has been misread.

## Out of scope
The other known differences of `IlLoweringParityTests` (`NullTestOfAConversion`, `UsingResource`,
`InterpolatedString`, `Lambda`, `StateMachine`). A least common base type of stores that disagree:
such a slot keeps `object`. Slots of a value type or holding an address. Turning `--il-fallback` on
by default.

## Notes
- Found by P2-068 (its Notes, "Surprise").
- Deviation: branched from `P2-068-forwarder-replaced-by-its-target` (PR #360), not from `main`. The
  sample and both known-difference entries exist only there, and on that branch P2-068 is in `done/`.
  The PR targets that branch until #360 merges.
- ILSpy types a slot by its first store: `s ?? "x"` stores `s` first and its slot is already `string`.
  `s?.Trim()` stores `ldnull` first, which is why its slot is `object`.
- Decision: a store's type is read off a call (its result, or the type a `newobj` makes), a variable
  (as ILSpy types it) and a string literal. Alternatives: every instruction's type; calls only. Rule:
  the smallest set that needs no lowering to name a type; any other store keeps the slot `object`,
  which is the IR this ticket found, valid and merely harder to prove.
- Finding, not fixed here: `c?.f` and `a?[0]` as an argument store an `LdObj`, so their slots stay
  `object` and still read the `object` casts. `x?.Property` is a call and is covered.
- Decision (criterion 3): `IlLowererTests.Signature` no longer walks a branch against a condition the
  path already branched on. With the slot typed, the two lowerings of `BlankTrimmed` have the same
  parameters and the same calls, but the IOperation lowering null-checks `Trim`'s receiver behind
  the `s is null` test, on the same SSA flag, and the compiler emits a plain `call` with no check.
  That dead `throw` was a path with no call the IL does not have. Alternatives: keep the entry;
  drop the check in `IrLowerer` (changes other samples' IR, criterion 6). Rule: the test compares
  call paths, and a path that takes one flag both ways is not one. Z3 proves the pair Equivalent
  (criterion 4).
- Local quirk: `IlLoweringParityTests` fails in a fresh worktree until the samples are restored
  (`build.ps1`'s "restore samples" step); `samples/version-bump/legacy` then loads.
- Renumbered from P2-108 while the PR was open: `main` took that id for the `nint` identity ticket
  first. The branch keeps its old name. P2-068 was squash-merged into
  `P2-068-adr-forwarder-is-its-target` meanwhile, which is the PR's base now.
