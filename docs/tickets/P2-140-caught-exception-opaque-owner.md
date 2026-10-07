# P2-140 The uses of a caught exception are counted, and the largest one lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The exception object a `catch` variable or a `when` filter reads is not modelled: every read of it
is an opaque node with reason `CaughtException` (`IOPERATION-COVERAGE.md`), and no ticket owns the
reason. P1-028's row (`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246
changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `CaughtException` | 125 / 124, 46, 11 | 124 / 124, 35, 8 | 228 / 228, 32, 5 | 113 | 24 (1.1%) | 51 (2.3%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns. The later censuses in the same report give 41 of 1,743 (2.4%). It also comes with two
smaller reasons of the same region of the lowering, `call-throw-in-try` (in 21 changed pairs) and
`rethrow` (in 8). Count what bodies do with the exception they catch, and lower the largest use.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `CaughtException`, `Try`, `Throw`;
VERIFICATION-MODEL.md, the sections on exceptions and on the call trace; ADR 0034 (per-ticket unlock
rule); `docs/tickets/done/M4-008-*.md` (filters); `docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `CaughtException` opaque nodes of `gitextensions-8522`'s changed pairs
   (a `--lower-only` run) by what reads the exception: an argument of a call (a logger, a message
   box), the inner exception of a `throw new T(..., ex)`, a property read (`Message`, `HResult`,
   `InnerException`, another), a `when` filter, `throw ex`, a type test or cast, an assignment to a
   local or field, other; and by what raised it where the body shows that: a call, a `throw` in the
   same `try`, either. For each use give the nodes, the changed pairs it is in, and the changed
   pairs it alone keeps opaque. Counts in `## Notes` (uses and exception type names only).
2. The use with the most changed pairs alone lowers to IR. The exception a `catch` receives becomes
   a value both sides can name: the object a `throw` in the same `try` raised where there is one,
   and otherwise a value that is a function of the call that threw, so that two sides that catch
   after the same calls hold the same exception. Decide the representation with `equiv-decide` and
   log it; a new IR node, a new sort or a change to what a `threw` edge carries goes through
   `equiv-adr`'s bar test first.
3. A test per lowered use in `tests/Equiv.Frontend.CSharp.Tests`, one pair that must stay Divergent
   because the two sides pass different exceptions on, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` rows updated.
4. On a re-run, the changed pairs of `gitextensions-8522` that hold `CaughtException` fall by at
   least 2% of changed pairs, or `## Notes` records why not.
5. Each remaining use at or above 5% of changed pairs alone is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, `src/Equiv.Core/` and `src/Equiv.Verify.Z3/` only as
criterion 2's decision requires (`equiv-extend-ir`), their tests,
`docs/tickets/IOPERATION-COVERAGE.md`, `docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 2 needs an accepted ADR and none exists when the split is done, write the ADR, record
the split and stop: the lowering is then its own ticket. More than one use lowered means the ticket
was misread.

## Out of scope
`call-throw-in-try` and `rethrow`, unless the lowered use cannot be written without one of them.
Stack traces, and any property of the exception whose value depends on where it was thrown.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): fifth by marginal unlock.
