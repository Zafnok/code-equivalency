# P2-214 An unedited `extern` method on a pair that leaves .NET Framework is Unknown, `no-body`, whatever it imports
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-165 (done, ADR 0058) lets an `extern` pair that crosses a runtime be Equivalent by congruence when no entry of
the `marshalling` list inside the pair's interval reaches its declaration. It kept ADR 0054 decision 4 for every
interval that reaches .NET Framework, because the breaking-change pages have no Interop category for the move from
.NET Framework to .NET Core and the marshaller differs there (the last error is cleared before a `SetLastError`
call on .NET only; built-in COM and WinRT).

P2-212's run of `gitextensions-8522` (equiv `bfbf413e`, compare mode quick,
`docs/runs/2026-10-15-full-gitextensions-8522/SUMMARY.md`) has `no-body` alone in 51 of its 870 changed pairs
(5.9%), above ADR 0028's 5%, and no open ticket owns it since P2-165. All 51 are Unknown, all are `runtime-rule` by
`properties.notCongruent`, and all are `[DllImport]` declarations whose text is the same on both sides. On
`duplicati-3124` the same reason is alone in 19 of 655 (2.9%), and on `openra-17989` in 6 of 225 (2.7%).

When done, the .NET Framework boundary has the list P2-165 wrote for the later versions, and an import that no
entry reaches is congruent on a pair that leaves .NET Framework too.

## Spec references
ADR 0054 decision 4, ADR 0058 (decision 3's table and the boundary it kept), `docs/tickets/done/P2-165-*.md`
(Notes: why the boundary was kept), `docs/runtime-changes-review.md`, `src/Equiv.Core/RuntimeChanges/runtime-changes.json`
(`marshalling`), `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The differences between the .NET Framework 4.x marshaller and .NET Core 3.0's are listed from the
   `DllImportAttribute`, `MarshalAsAttribute` and interop documentation (the `SetLastError` clearing, built-in COM,
   WinRT, and any default that changed), each with the declarations it reaches, as ADR 0058's table does. Where the
   list cannot be shown complete, `equiv-adr` records it and decision 4 stays for that case.
2. The entries are in the `marshalling` list with a `changedIn` at the boundary, and the review file agrees
   (`ReviewFileAndTableAgree`).
3. `gitextensions-8522` rerun in compare mode quick; Notes give `no-body` alone next to 51, and each of the 51 by
   the entry that reaches it or "congruent".

## Tests
The runtime-change table tests; a sample pair of a .NET Framework and a .NET 5 import, one reached and one not.

## Size guard
Modelling the native function, or a new rule id: stop.

## Out of scope
`InternalCall` (ADR 0058 keeps decision 4 for it). `[LibraryImport]`.

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
