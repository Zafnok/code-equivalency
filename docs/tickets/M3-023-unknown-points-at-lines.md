# M3-023 An Unknown result points at the lines that made it Unknown
Status: todo (blocked on ADR 0027 acceptance)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-016

## Goal
ADR 0027 decision 4. A reviewer of an Unknown result should read the lines that caused it, not
the whole method. Every opaque node reached and every abstraction depended on becomes a
`relatedLocation`. The primary location moves to the first one on the modern side.

## Spec references
ADR 0027; ADR 0010 (partialFingerprints); ADR 0014; ADR 0026; VERIFICATION-MODEL section 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Unknown` carries `ImmutableArray<SourceSpan> Causes` with a side marker. Opaque causes come
   from the reached `IrOpaque` spans. Abstraction causes come from M3-016's `abstractions`.
2. The SARIF writer emits each cause as a `relatedLocation` whose message is the reason, and sets
   the primary location to the first modern-side cause. With no modern-side cause, it keeps the
   procedure location.
3. `partialFingerprints` and `baselineState` are unaffected. A test runs a baseline round trip
   where only the cause moves and asserts `unchanged`.
4. On `business-layer`, each remaining Unknown points at its construct's line. Snapshot updated.

## Files
`src/Equiv.Core/Verdicts/Unknown.cs`, `src/Equiv.Core/Reporting/*`,
`src/Equiv.Verify.Z3/Z3Backend.cs` (collect causes), `tests/**`.

## Tests
`UnknownListsEveryReachedOpaqueSpan`, `PrimaryLocationIsFirstModernCause`,
`NoModernCauseKeepsTheProcedureLocation`, `MovingACauseKeepsTheBaselineUnchanged`.

## Size guard
No encoder change beyond reporting which opaque nodes were reachable.

## Out of scope
Code Scanning upload (M3-004's `action.yml`).

## Notes
