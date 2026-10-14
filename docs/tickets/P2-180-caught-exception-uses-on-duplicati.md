# P2-180 The uses of a caught exception that keep Duplicati's changed pairs opaque are counted, and the largest one lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-140 made a `catch` variable that is only read as `e.Message` a value, and left every other use of a
caught exception an opaque node with reason `CaughtException`. It measured the uses on
`gitextensions-8522` only, where no other use reached 1% of changed pairs, and filed nothing.

P2-130's run of `duplicati-3124` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-13-full-duplicati-3124/SUMMARY.md`), the pair's first run to a result, has
`CaughtException` alone in 37 of 707 changed pairs (5.2%), with `rethrow` in 19 more, and in 45
Unknown results (6.4% of changed pairs): the pair's largest opaque reason, above ADR 0028's 5%, and no
open ticket owns it. `rethrow` (in 31 Unknown results, 4.4%) comes with it.

When done, the use of a caught exception that keeps the most of Duplicati's changed pairs opaque is
lowered, or decided to stay opaque, with the count that chose it.

## Spec references
`docs/tickets/done/P2-140-*.md` (its table of uses and its decisions), ADR 0018 and its clarification
for the thrown exception, `IOPERATION-COVERAGE.md` (`CaughtException`, `rethrow`),
`.claude/skills/equiv-extend-ir`, `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `duplicati-3124` at `main`'s head, P2-140's table of uses (and of raises whose exception
   is not a value), plus `rethrow`, over the changed pairs: in, and alone.
2. Lower the largest use that is not already lowered, or record with `equiv-decide` why it stays
   opaque and lower the next.
3. Rerun `duplicati-3124` in compare mode quick and record, in Notes, `CaughtException` alone and in
   Unknown results next to 37 and 45.

## Tests
Lowering tests in `tests/Equiv.Frontend.CSharp.Tests`, and a pair test in
`tests/Equiv.Tests.Integration/CaughtExceptionEquivalenceTests.cs`, for the lowered use, including two
bodies that differ in what they do with the exception and must not prove.

## Size guard
A new IR node or sort: stop and route it through `equiv-adr`.

## Out of scope
Stack traces; exceptions handed to a logger.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): the largest opaque reason of `duplicati-3124` with no open
  owner.
