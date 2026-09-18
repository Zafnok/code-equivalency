# M1-003 verdicts matching config
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-002

## Goal
Verdict types; ProcedureIdentity and its normalisation (namespace map, generic arity, parameter types); IProcedureMatcher producing pairs plus Added and Removed; equiv.config.json schema (rename maps, call identity maps, bound, timeout) with validation and helpful errors. Property test: matching is symmetric and total over generated identity sets.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] `Verdict` closed hierarchy in `Equiv.Core.Verdicts`: `Equivalent`, `Divergent(Counterexample)`,
      `Unknown(UnknownReason, string Detail)`, `Added`, `Removed` (ARCHITECTURE.md; nothing else).
      `Counterexample(IrInputs, IrRun Old, IrRun New)` reuses `IrRun` for outcome+outs+trace.
- [ ] `Equiv.Core.Matching`: `ProcedureIdentityNormalizer.Member(...)`/`.Endpoint(...)` build the
      assembly-agnostic identity string (namespace/type rename map, generic arity, parameter types,
      or `VERB /route`); `ProcedurePair(Old, New)`; `MatchResult(Pairs, Added, Removed, Ambiguous)`;
      `IProcedureMatcher`; `StableIdentityMatcher` implementation (exact-identity matching, duplicate
      identities on one side are Ambiguous per VERIFICATION-MODEL.md section 4).
- [ ] `Equiv.Core.Configuration`: `EquivConfig`, `RenameMap`, `EquivConfigDiagnostic(s)`,
      `EquivConfigResult`, `EquivConfigParseException`, `EquivConfigLoader.Load(json)` — hand-rolled
      `JsonDocument` walk (no reflection, no new package), diagnostics for bad root/bound/timeout/
      rename entries/unknown properties, defaults substituted so callers can proceed after a warning.
- [ ] `IVerificationBackend`/`VerificationOptions` stubs in `Equiv.Core` (ticket M3-001 finalises them).
- [ ] tests: unit per diagnostic id and normalisation case; property test (`Equiv.Core.Tests`,
      inline CsCheck generator) that `StableIdentityMatcher.Match` is symmetric (swapping old/new
      swaps Added/Removed, keeps Pairs/Ambiguous) and total (every identity in old ∪ new lands in
      exactly one of Pairs/Added/Removed/Ambiguous) over generated identity multisets.

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
