# P2-062 An opaque fragment in `properties.abstractions` names its reason
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
An `abstraction` Unknown lists the abstractions its verdict depends on (ADR 0026). Each entry is an
`identity` (an `IrPure` operator name such as `f32.mul`, or `opaque:<fingerprint>`), a `side` and a
`span`. P2-046's "Top abstractions" line could group the `IrPure` operators by name but not the
opaque fragments: 503 of the 743 entries on Git Extensions are `opaque:` fingerprints, and nothing
says whether they are a lambda, a switch pattern or an interpolated string. Without that, the
histogram cannot say which construct's lowering would remove the most abstraction Unknowns. Add the
opaque reason to each fragment entry.

## Spec references
ADR 0026 (`properties.abstractions`), `src/Equiv.Core/Reporting/SarifReportWriter.cs`,
`docs/VERIFICATION-MODEL.md` section 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each `properties.abstractions` entry whose identity starts with `opaque:` also carries
   `reason`, the opaque reason of the fragment (the `IrOpaque` reason string, for example
   `DelegateCreation`). An `IrPure` entry has no `reason`.
2. The SARIF schema test and the report snapshot include the new field.
3. `equiv-corpus-run`'s SUMMARY template line "Top abstractions" is reworded to group opaque entries
   by `reason`.

## Files
`src/Equiv.Core/Reporting/SarifReportWriter.cs` and whatever supplies the fragment's reason,
`tests/Equiv.Core.Tests`, `.claude/skills/equiv-corpus-run/SKILL.md`.

## Tests
`SarifReportWriterTests.AnOpaqueAbstractionCarriesItsReason`, `.APureAbstractionCarriesNoReason`.

## Size guard
If the reason is not available where the abstraction is recorded, stop and write where it is lost.

## Out of scope
Changing what counts as an abstraction, or the fingerprint.

## Notes
- Found by P2-046.
- Size guard did not trip. The replay only knows the call identity (`ModelDecoder.Abstractions`), but
  `ProductEncoder.ShareFragments` still holds the `IrOpaque` it rewrote into the `opaque:` call, and
  `SharedFragments.Locate` already attaches that fragment's span there. The reason travels the same way.
- Decision: `Abstraction` gets an optional `Reason` (null for an `IrPure` operator) and
  `SharedFragments.Occurrences` carries the fragment's reason beside its span. Reason: `Locate` is the one
  place that has both the abstraction and its `IrOpaque`, so no new plumbing is needed.
- Decision: `Locate` now sets the reason even when the abstraction already has a span (it used to return
  such an abstraction untouched). Reason: criterion 1 says every `opaque:` entry carries `reason`.
- Decision: when a side has the same fingerprint more than once, the reason is the first occurrence's, as
  the span already is. Reason: one entry per identity and side; the fingerprint is out of scope.
- `reason` is an additive SARIF property, so the commit carries `Release: minor`.
