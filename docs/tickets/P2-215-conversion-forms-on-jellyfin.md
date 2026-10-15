# P2-215 The conversions that keep Jellyfin's changed pairs opaque are counted, and the largest form lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-095 and P2-123 (done) lowered the `Conversion` forms they counted on `gitextensions-9860` and
`gitextensions-8522`, and the reason has no open owner.

P2-212's run of `jellyfin-13023` (equiv `bfbf413e`, compare mode quick,
`docs/runs/2026-10-15-full-jellyfin-13023/SUMMARY.md`) has `Conversion` alone in 8 of its 122 changed pairs
(6.6%), above ADR 0028's 5%, and in 3 Unknown results (`opaque`, review group `opaque:Conversion`). It is the
pair's largest opaque reason in the census too (634 bodies per side). Jellyfin's changed pairs are few (the pull
request only bumps the target framework), so a small count is a large share.

When done, the form that keeps the most of Jellyfin's changed pairs opaque is lowered, or decided to stay opaque,
with the count that chose it.

## Spec references
`IOPERATION-COVERAGE.md` row `Conversion`, `docs/tickets/done/P2-095-*.md`, `docs/tickets/done/P2-123-*.md`,
`.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `jellyfin-13023` at `main`'s head, the `Conversion` nodes of its changed pairs by form (source and
   target type kind, user-defined operator, checked), in and alone.
2. Lower the largest form, or record with `equiv-decide` why it stays opaque and lower the next.
3. Rerun `jellyfin-13023` in compare mode quick and record, in Notes, `Conversion` alone next to 8.

## Tests
`IrLowererTests` and `LoweringOracleTests` for the lowered form.

## Size guard
A new sort: stop and route it through `equiv-adr`.

## Out of scope
Conversions in other pairs' changed pairs, unless the same form.

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
