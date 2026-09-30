# P2-060 A lambda or method group converted to a delegate no longer keeps a changed pair opaque
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-046

## Goal
P2-046's Git Extensions run shows `DelegateCreation` as the single largest reason a changed pair is
opaque: 194 of 1,143 changed pairs (17.0%) carry no other reason, and 328 (28.7%) carry it with
others. ADR 0034 item 2's unlock rule needs an owner for any reason at or above 5%, and this one has
none. M4-004 (done) shares a fragment that is identical on both sides, so an unchanged lambda is
already a shared call; what remains is a pair whose lambda differs, or whose fragment could not be
shared (a capture written later, a runtime-sensitive body). Those keep the whole method opaque.
Decide how a delegate's body is lowered so that a changed lambda contributes a verdict instead of an
opaque, and lower it. Sized as one ticket only after the first criterion's measurement.

## Spec references
ADR 0024 (shared opaque fragments), ADR 0034 (per-ticket unlock rule), ADR 0039 (IL fallback: its
reason list does not include `DelegateCreation`), `docs/tickets/IOPERATION-COVERAGE.md` rows
`DelegateCreation`, `AnonymousFunction`, `TranslatedQuery`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the 194 `DelegateCreation`-only changed pairs by why the fragment is not
   shared, from a `--lower-only` run of the `gitextensions-8522` pair (`equiv-corpus-run`, census
   mode): lambda body differs between the sides, capture written after creation, runtime-sensitive
   body, method group, other. Put the counts in `## Notes`. If one cause is under 20% of them, say
   so and stop; the ticket is then a candidate for closing without code.
2. Route the design through `equiv-adr`'s bar test (a new fragment or inlining scheme is a change
   to ADR 0024's rule for what is shared) and log the outcome as a `Decision:` line.
3. The chosen construct lowers to IR with a test per `IOPERATION-COVERAGE.md` row it changes, and the
   row is updated.
4. On a re-run of the same census, `changedReasonSets["DelegateCreation"]` falls by at least 5% of
   changed pairs (57 pairs) or the ticket records why not. The figure goes in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/` (the lowering of the chosen construct), `docs/tickets/IOPERATION-COVERAGE.md`,
and an ADR only if criterion 2 says so.

## Tests
Named per changed `IOPERATION-COVERAGE.md` row, in `tests/Equiv.Frontend.CSharp.Tests`.

## Size guard
If criterion 1 shows the lambda bodies differ in ways the solver cannot compare (they call
different members), that is not lowering work: stop and write the finding in `## Notes`.

## Out of scope
The IL fallback (P1-014 to P1-016), `TranslatedQuery` (P2-026), other opaque reasons.

## Notes
- Found by P2-046 (`docs/runs/2026-09-30-full-verdict.md`, per-ticket unlock table).
