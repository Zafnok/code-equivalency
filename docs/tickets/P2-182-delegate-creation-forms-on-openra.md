# P2-182 The `DelegateCreation` forms that keep OpenRA's changed pairs opaque are counted, and the largest one lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-136 counted the forms behind `DelegateCreation` on `gitextensions-8522`, lowered the largest, and
found no other form at 5% of that pair's changed pairs. The reason has no open owner.

P2-130's run of `openra-17989` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-14-full-openra-17989/SUMMARY.md`), the pair's first run to a result, has
`DelegateCreation` alone in 31 of its 321 changed pairs (9.7%), above ADR 0028's 5%: the pair's
largest opaque reason set. It is in 9 Unknown results (8 `opaque`, 1 `abstraction`), and `delegate` is
the largest abstraction kind of the pair's `abstraction` Unknowns (43 of 99).
`IOPERATION-COVERAGE.md` row `DelegateCreation` names what stays opaque: a lambda whose capture is
stored after it, a conversion with no fingerprint, and a method group whose receiver is evaluated.

When done, the form that keeps the most of OpenRA's changed pairs opaque is lowered, or decided to
stay opaque, with the count that chose it.

## Spec references
`docs/tickets/done/P2-136-*.md` and `P2-067-*.md`, ADR 0025, `IOPERATION-COVERAGE.md` row
`DelegateCreation`, `.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `openra-17989` at `main`'s head, the `DelegateCreation` nodes of the 31 changed pairs by
   form (P2-136's split), in and alone.
2. Lower the largest form, or record with `equiv-decide` why it stays opaque and lower the next.
3. Rerun `openra-17989` in compare mode quick and record, in Notes, `DelegateCreation` alone next to 31.

## Tests
`DelegateLoweringTests` for the lowered form, including two delegates that differ and must not be one
function.

## Size guard
A new IR node or sort: stop and route it through `equiv-adr`.

## Out of scope
Runtime-sensitive lambdas, which are their side's own function by ADR 0025.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): an opaque reason at or above 5% of `openra-17989`'s changed
  pairs with no open owner.
