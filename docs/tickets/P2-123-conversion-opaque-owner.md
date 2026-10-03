# P2-123 The conversion forms behind `Conversion` are counted, and the largest one without a ticket lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
A conversion the frontend does not lower is an opaque node with reason `Conversion`: "anything else"
in the `Conversion` row of `IOPERATION-COVERAGE.md`, which names a nullable conversion and an
unboxing among them. P2-099 took collection expressions and target-typed `new()` out of the reason.
What is left, alone, keeps this share of changed pairs opaque:
- `gitextensions-9860` (pull request 371's run): 110 of 722 (15.2%), and it is in at least 69 more
  with one other reason;
- `jellyfin-13023` (the same pull request): 29 of 573 (5.1%);
- `gitextensions-11372`: 22 of 351 (6.3%), in 650 bodies per side, P2-099's rerun;
- `gitextensions-8522`: 22 of 1,143 (1.9%), in 100, P2-046's run.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. The run reports name
P1-014, the IL fallback, which P1-018 left off by default, or "none". P2-095 owns one form, a
conversion to `Nullable<T>`. Count the forms, and lower the largest one P2-095 does not own.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `Conversion`; ADR 0034 (per-ticket unlock rule); M4-002
(`conv.*` functions), M4-005 (casts as type tests), M3-010 (the `cast` maps).

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `Conversion` opaque nodes of `gitextensions-9860`'s changed pairs (a
   `--lower-only` run) by form: to `Nullable<T>`, from `Nullable<T>`, unboxing, boxing of a type
   parameter, enum to or from its underlying type, a user-defined conversion whose operand or result
   is not its method's own type, pointer or `nint`, `dynamic`, other. For each form give the nodes,
   the changed pairs it is in, the changed pairs it alone keeps opaque, and the ticket that owns it
   or "none". Counts in `## Notes` (conversion kinds and types only).
2. The form with the most changed pairs alone and no owning ticket lowers to IR. Decide the
   representation with `equiv-decide` and log it; if it needs an IR node or a sort the IR does not
   have, go through `equiv-adr`'s bar test first.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, `changedReasonSets["Conversion"]` on `gitextensions-9860` falls by at least 5% of
   changed pairs (36 pairs), or `## Notes` records why not.
5. Each remaining form at or above 5% of changed pairs alone and with no owner is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that the form P2-095 owns holds more than two thirds of the changed pairs
alone, lower nothing: record the split and stop. More than two forms lowered means the ticket was
misread.

## Out of scope
The IL fallback. A conversion to `Nullable<T>` (P2-095). Collection expressions (P2-120).

## Notes
- Found by the 2026-10-03 review of the open tickets against the Unknown reasons of the three large
  runs. `Conversion` alone is the largest reason set on `gitextensions-9860` after the pairs with no
  opaque, and it has no open owner.
- `gitextensions-9860` is in `tools/corpus/pairs.csv` once P2-066 is done, hence the dependency.
