# P2-083 Lowering a binary operator in a branch condition crashes with a bare `NullReferenceException`
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-065's runs found 19 pair-level crashes with the message `Lowering ... failed: Object reference
not set to an instance of an object.`: 3 on `eshop-manual`, 8 on `openra-17989`, 8 on `duplicati-3124`.
All 19 stacks pass through `IrLowerer.Branch` (`IrLowerer.cs` line 521) and end in one of two places:
- `PureCatalogue.Binary` (`PureCatalogue.cs` line 104), from `IrLowerer.Binary` line 1366: 9 pairs.
  Line 104 reads `left.SpecialType` and `right.SpecialType`, and line 1366 passes
  `binary.LeftOperand.Type!` and `binary.RightOperand.Type!`, so an operand has no type.
- `IrLowerer.Binary` line 1349 (the `NullTest` path): 10 pairs.

The three eShop methods are `eShopLegacyMVC.Controllers.CatalogController::Details(int?)`,
`::Edit(int?)` and `::Delete(int?)`; each takes an `int?` and tests it against `null` in an `if`.
The OpenRA and Duplicati identities are in their SUMMARY files. Find the operand shape, and lower it
or make it an `IrOpaque` with reason `Binary`. Lowering never throws (CLAUDE.md, task-loop rules).

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `Binary`, ADR 0023, P2-034 (the previous bare
`NullReferenceException` in lowering, and how its repro was written),
`docs/runs/2026-10-01-full-eshop-manual/SUMMARY.md`, `docs/runs/2026-10-01-full-openra-17989/SUMMARY.md`,
`docs/runs/2026-10-01-full-duplicati-3124/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test in `tests/Equiv.Frontend.CSharp.Tests` reproduces each of the two stack ends with a C#
   method written for the test. The shapes go in `## Notes` (operation kinds and types only).
2. Both lower without throwing: to IR where the row says the form is lowered, otherwise to an
   `IrOpaque` with reason `Binary`. The `Binary` row in `IOPERATION-COVERAGE.md` says which.
3. A `--lower-only` run of `eshop-manual` has no entry in `properties.unverified`. For `openra-17989`
   and `duplicati-3124`, the count of lowering notifications goes in `## Notes`; any that remain have
   another stack and get their own ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`, `src/Equiv.Frontend.CSharp/Lowering/PureCatalogue.cs`,
their tests, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 1.

## Size guard
If the two stack ends have unrelated causes and the second needs more than the first's guard, split it.

## Out of scope
Lowering lifted operators in general (P2-087). The run-level crash on the same pairs (P2-082).

## Notes
- Found by P2-065, at equiv ef79ff6. Git Extensions had no lowering crash at bd8e379 (P2-046).
