# P2-199 A property a body only writes does not fire its getter's runtime row in the fingerprint
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The bound fingerprint renders a property reference by all its accessors (`BoundSerialiser.Render`,
`IPropertySymbol` arm: getter and setter), so a body that only writes `ProcessStartInfo.UseShellExecute`
names the `System.Diagnostics.ProcessStartInfo::get_UseShellExecute(` row and is not congruent
(`properties.notCongruent` component `runtime-rule`). The lowering calls the setter, which has no row,
so the solver never sees a runtime-changed call and proves the body: the rule costs a solver run and,
where the solver gives up, an Unknown, never a Divergent.

P2-190's reruns (compare mode quick, `--jobs 4`) found 9 results whose only runtime rule is that
getter: 8 proved by the solver and 1 Unknown (`ProcessController::StartSleepPrevention()`, opaque), on
`duplicati-3124` (`OSInfoHelper::RunProgramAndReadOutput`, `Platform::_RetrieveUname`,
`MacOSPowerSupplyState::GetSourcePmset` and `GetSourceIoreg`, `Utility::Which`,
`SynologyAuthenticationHandler::ShellExec`, `RumpsRunner::Restart`, `StartSleepPrevention`) and
`openra-17989` (`Platform::GetCurrentPlatform()`). The seven P2-190 read write `UseShellExecute` in an
object initializer or a statement and never read it.

When done, a property reference flags the row of the accessor it calls, as the lowering does: the
getter for a read, the setter for a write, both for a compound assignment or an increment.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed members, the fingerprint and the lowering agree),
`src/Equiv.Frontend.CSharp/Fingerprinting/BoundSerialiser.cs` (`Render`, `Method`),
`docs/tickets/done/P2-190-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A body that only writes a property whose getter has a row is congruent with its equal text on a
   pair that crosses the row; one that reads it is not.
2. The lowering and the fingerprint agree on every case (the `Fires` helper of P2-114's and P2-190's
   tests).
3. Rerun `duplicati-3124` in compare mode quick; Notes give the 9 results' new outcomes.

## Size guard
A change to how any symbol other than a property's accessors is rendered: stop.

## Out of scope
Which rows exist.

## Notes
- Filed 2026-10-14 by P2-190 from its reruns.
