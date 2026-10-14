# P2-183 The array element reads that keep OpenRA's changed pairs opaque are counted, and the largest form lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
An array element reference lowers as a map read (P2-001) unless the array has several dimensions, the
index is a `long`, or the array is neither a plain variable nor an array creation: those are opaque
with reason `ArrayElementReference` (`IOPERATION-COVERAGE.md`). No ticket owns the reason.

P2-130's run of `openra-17989` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-14-full-openra-17989/SUMMARY.md`), the pair's first run to a result, has
`ArrayElementReference` alone in 19 of its 321 changed pairs (5.9%), above ADR 0028's 5%, and in 13
Unknown results (8 `opaque`, 5 `abstraction`). OpenRA is a game engine; its map and cell grids are
arrays, and the reason is in 245 bodies per side.

When done, the form that keeps the most of OpenRA's changed pairs opaque is lowered, or decided to
stay opaque, with the count that chose it.

## Spec references
`IOPERATION-COVERAGE.md` row `ArrayElementReference`, `docs/tickets/done/P2-001-*.md`,
`.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `openra-17989` at `main`'s head, the `ArrayElementReference` nodes of its changed pairs by
   form (several dimensions, a `long` index, the kind of expression the array is), in and alone.
2. Lower the largest form, or record with `equiv-decide` why it stays opaque and lower the next.
3. Rerun `openra-17989` in compare mode quick and record, in Notes, `ArrayElementReference` alone next
   to 19.

## Tests
`IrLowererTests` and `LoweringOracleTests` for the lowered form, and a pair test where two bodies read
different elements and must not prove.

## Size guard
A new sort for arrays: stop and route it through `equiv-adr`.

## Out of scope
Spans and `stackalloc`.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): an opaque reason at or above 5% of `openra-17989`'s changed
  pairs with no open owner.
