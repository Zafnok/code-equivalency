# P2-086 An interpolated string no longer keeps a changed pair opaque
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: none

## Goal
`InterpolatedString` is an opaque node. Alone, it keeps this share of changed pairs opaque:
- `eshop-manual`: 5 of 26 (19.2%), P2-065's run;
- `gitextensions-8522`: 26 of 1,143 (2.3%), P2-046's run;
- `duplicati-3124`: 20 of 936 (2.1%), P2-065's census.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. P2-046 named ADR
0039's IL fallback as the owner. P1-018 has since measured the fallback and left it off by default,
so the reason has no open owner.

`IOPERATION-COVERAGE.md` says why a migration hits it: the same interpolated string binds to
`string.Format` on .NET Framework and to `DefaultInterpolatedStringHandler` on modern .NET, so the
two fragments fingerprint differently and are not shared (M4-004). Unchanged source text therefore
becomes a changed, opaque pair exactly when the runtime changes. Lower an interpolated string so that
the same text and the same holes are the same value on both sides whichever way it binds.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `InterpolatedString`, ADR 0024 (shared opaque fragments),
ADR 0041 (closed calls; `string + string` is already `System.String::Concat`), ADR 0034 (per-ticket
unlock rule), `docs/runs/2026-10-01-full-eshop-manual/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `InterpolatedString`-only changed pairs of `eshop-manual` and
   `duplicati-3124` (a `--lower-only` run each) by shape: holes of string type only, holes with a
   format or alignment clause, holes of other types, a handler other than the default one. Counts in
   `## Notes`.
2. Route the representation through `equiv-decide`, and through `equiv-adr`'s bar test if it adds an
   IR node or a culture assumption. Log the `Decision:` line. A format or alignment clause, or a hole
   whose formatting depends on the current culture, stays opaque unless the decision covers it.
3. The chosen shapes lower to the same IR for a `string.Format` binding and for a handler binding of
   the same text. Snapshot test with both bindings; the `IOPERATION-COVERAGE.md` row is updated.
4. On a re-run, `changedReasonSets["InterpolatedString"]` on `eshop-manual` falls, or `## Notes`
   says why not. The figures for all three pairs go in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows most holes carry a format clause or a non-string type, stop after criterion 2
and record the finding.

## Out of scope
The IL fallback. `string.Format` calls written by hand.

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-migrations-verdict.md`).
