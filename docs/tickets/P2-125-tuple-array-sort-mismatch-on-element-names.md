# P2-125 Verifying crashes on two tuple array types that differ only in tuple element names
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
In the `full` run of `openra-17989` on 2026-10-03 at 550e260 (P2-121's criterion 4; first seen in P2-082's
run), one pair, `OpenRA.ObjectCreator::.ctor(OpenRA.Manifest,OpenRA.InstalledMods)`, fails in the `verify`
phase with a Z3 error of the family `domain sort ... and parameter sort ... do not match`. The two sorts
are array-of-tuple types with the same element types in the same order; they differ only in the tuple
element names. It is the run's only `toolExecutionNotification` and its only `properties.unverified`
entry, and it is why the run exits 5 (ADR 0023). When this is done, tuple element names do not take part
in a sort's name, the pair verifies to a verdict, and the run has no pair-level failure left.

This is the same family as P2-031 (inherited `this`), P2-032 (`T[]` against `T[]?`) and P2-033: two
spellings of one type become two sorts, and the crash appears where they meet in a shared declaration.
A likely cause, to be confirmed and not assumed: `TypeMapper` names every non-named type (arrays among
them) by `ToDisplayString(TypeMapper.Unannotated)`, and that display format writes tuple element names,
so an array of a tuple with names and an array of the same tuple with other names, or none, get
different sort names. P2-032 removed the nullable annotation from that format for the same reason.

## Spec references
ADR 0023 (a pair-level crash), `src/Equiv.Frontend.CSharp/Lowering/TypeMapper.cs` (`Unannotated`,
`MetadataName`, the tuple mapping), `src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs`
(`Constructed` uses the same format), `docs/tickets/done/P2-032-nullable-array-sort-mismatch.md` (its
Notes on the cause and on P2-042), `docs/tickets/done/P2-031-domain-sort-mismatch-inherited-this.md`,
`docs/tickets/done/P2-033-irsortvalue-key-not-found.md`,
`docs/tickets/done/P2-121-a-huge-unrolled-pair-overflows-the-native-stack.md` (Notes, criterion 4).

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, `## Notes` records the two sort names from the error and where they meet (a call's
   domain, a map's key, a field's type), found with a run of `openra-17989` (`equiv-corpus-run`) or from
   its SARIF notification. Identities and type and operation kinds only, no corpus source text.
2. A test in `tests/Equiv.Frontend.CSharp.Tests` maps an array of a tuple type with element names and an
   array of the same tuple type with different element names, and with none, to the same IR type.
3. A pair written for the test that has the shape of criterion 1 makes `Z3Backend.Verify` throw before
   the fix and return a verdict after it. The test lives in the project the shape needs
   (`tests/Equiv.Verify.Z3.Tests` or `tests/Equiv.Tests.Integration`).
4. `## Notes` says whether the gap is specific to arrays of tuples or covers every non-named type that
   holds a tuple (a tuple as a type argument in a generic call identity, a pointer, a type parameter's
   constraint). What the fix covers is tested; what it leaves is filed as a follow-up ticket.
5. A `full` run of `openra-17989` has no entry in `properties.unverified` and no
   `toolExecutionNotification`, and exits with a code other than 5. `## Notes` records the pair's
   verdict and the exit code.
6. The second observation in `## Notes` is looked into: `## Notes` records what `outcome=failed` means for
   `OpenRA.ModData::.ctor(OpenRA.Manifest,OpenRA.InstalledMods,bool)` in the `contracts` phase (the
   exception type and message, or the reason logged), whether it has the same cause as criterion 1,
   and whether it changes any result. If the cause is the same, criterion 5's run shows it gone. If it
   is another cause, it is filed as its own ticket and not fixed here.

## Files
`src/Equiv.Frontend.CSharp/Lowering/TypeMapper.cs`, its tests in `tests/Equiv.Frontend.CSharp.Tests`,
one test file for criterion 3, this ticket. `src/Equiv.Frontend.CSharp/Lowering/CallIdentityFactory.cs`
and its tests only if criterion 4 finds the same format is the cause there.

## Tests
`TypeMapperTests.TupleArraysMapToOneTypeWhateverTheElementNames`,
`TupleArraySortTests.ElementNamesDoNotSplitTheSort`.

## Size guard
More than two `src/` files changed, or a change to how sorts are compared in `src/Equiv.Verify.Z3`
rather than to how a type is named, means the ticket has been misread.

## Out of scope
The pair's verdict once it verifies (an Unknown or a Divergent is recorded, not fixed). A failure in
the `contracts` phase with another cause (criterion 6 files it). How a failed `contracts` outcome is
reported in the SARIF. The run time of `openra-17989` (P2-077, P2-101).

## Notes
- Found by P2-082's `full` run of `openra-17989` and seen again in P2-121's (2026-10-03, at 550e260:
  10,206 results, exit 5). P2-109 and P2-121 both put it out of scope.
- Second observation, same run, not looked into:
  `OpenRA.ModData::.ctor(OpenRA.Manifest,OpenRA.InstalledMods,bool)` logs `outcome=failed` in the
  `contracts` phase. It is not a notification and not an `unverified` entry, so the run's exit code
  does not show it. Its signature shares both parameter types with the failing pair's, which is a
  reason to check for the same cause, not evidence of one.
- Criterion 1, from the notification in P2-121's SARIF (no new run needed): the two sorts are
  `|(System.Reflection.Assembly <n1>, string <n2>)[]|` and the same type under two other element names. They meet in
  one side, not across sides: `Context.MkStore` in `FragmentEncoder.EncodeInstruction`, the `IrMapWrite` of a field
  assignment. The field is declared as an array of a tuple with explicit element names; the value assigned is the
  result of an invocation (`ToArray`) whose tuple element names are inferred, so the field's map has one sort for its
  values and the stored value another. Both sides have the shape.
- Cause, as the Goal guessed: `TypeMapper.MetadataName` names a non-named type by `ToDisplayString(Unannotated)`, and
  that format writes a tuple in tuple syntax with its element names.
- Decision: add `SymbolDisplayMiscellaneousOptions.ExpandValueTuple` to `TypeMapper.Unannotated` rather than strip
  the names from the symbol. A tuple inside a non-named type is then written as its `System.ValueTuple<...>`, which
  has no names, at any depth. One line, and the sort of a named tuple type itself (its metadata name) is unchanged.
- Criterion 3: in a Debug build the unfixed shape stops one step sooner than in the corpus run, at the lowerer's
  `Debug.Assert` "lowered IR must validate" (`IrValidator` already reports the ill-sorted write); a Release build has
  no assert and reaches Z3, which is the corpus crash. `TupleArraySortTests.ElementNamesDoNotSplitTheSort` failed on
  that assert before the fix and returns Equivalent after it.
- Criterion 4: not specific to arrays of tuples. The gap is every use of `Unannotated`: any non-named type that holds a
  tuple (an array at any rank or depth, a tuple nested in a tuple, a tuple as a type argument of the element type, a
  pointer to a tuple) and, through `CallIdentityFactory.Constructed`, a generic callee's type arguments, where
  `M<(T a, U b)>()` and `M<(T, U)>()` were two functions. The one change covers all of them and
  `CallIdentityFactory.cs` needed no edit. Tested: arrays, jagged arrays, nested tuples and a tuple type argument
  inside an array (`TypeMapperTests.TupleArraysMapToOneTypeWhateverTheElementNames`), and the generic call
  (`TupleArraySortTests.AGenericCallAndItsRenamedTupleTypeArgumentFormAreEquivalent`, not Equivalent before the fix).
  A pointer to a tuple takes the same arm of `MetadataName` and has no test of its own. Nothing is left to file.
- Criterion 6: `outcome=failed` in `contracts` is `CompareCommand.UnderContracts` catching an exception from
  `VerifyUnderContracts`; the pair keeps the verdict it had and a `warning:` line goes to the console, with no
  notification and no `unverified` entry. For `OpenRA.ModData::.ctor(OpenRA.Manifest,OpenRA.InstalledMods,bool)` the
  console line carries the same `Z3Exception` with the same two sorts: it calls the failing constructor, and
  verifying it under callee contracts encodes that callee. Same cause as criterion 1; it changed no result.
