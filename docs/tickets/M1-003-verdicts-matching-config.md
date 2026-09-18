# M1-003 verdicts matching config
Status: done (PR #19)
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-002

## Goal
Verdict types; ProcedureIdentity and its normalisation (namespace map, generic arity, parameter types); IProcedureMatcher producing pairs plus Added and Removed; equiv.config.json schema (rename maps, call identity maps, bound, timeout) with validation and helpful errors. Property test: matching is symmetric and total over generated identity sets.

## Spec references
ARCHITECTURE.md ("Matching contracts", "Verdicts", C# frontend and CLI bullets, data flow diagram);
VERIFICATION-MODEL.md sections 1 (claim/proofMethod), 3 (migration normalisations, rename maps,
runtime-changes suppression), 4 (matching), 6 (verdict/SARIF table, referenced for scope only —
SARIF emission itself is M1-004); ticket M1-002 Design (`ProcedureIdentity` placeholder) and Notes
(decision log style, `IrEquality`); ticket M3-001 Design (`IVerificationBackend`/`VerificationOptions`
stub shape, `Counterexample` shape); CLAUDE.md (no reflection, no new package without an ADR row).

## Deliverables
- [x] `Verdict` closed hierarchy in `Equiv.Core.Verdicts`: `Equivalent`, `Divergent(Counterexample)`,
      `Unknown(UnknownReason, string Detail)`, `Added`, `Removed` (ARCHITECTURE.md; nothing else).
      `Counterexample(IrInputs, IrRun Old, IrRun New)` reuses `IrRun` for outcome+outs+trace.
- [x] `Equiv.Core.Matching`: `ProcedureIdentityNormalizer.Member(...)`/`.Endpoint(...)` build the
      assembly-agnostic identity string (namespace/type rename map, generic arity, parameter types,
      or `VERB /route`); `ProcedurePair(Old, New)`; `MatchResult(Pairs, Added, Removed, Ambiguous)`;
      `IProcedureMatcher`; `StableIdentityMatcher` implementation (exact-identity matching, duplicate
      identities on one side are Ambiguous per VERIFICATION-MODEL.md section 4).
- [x] `Equiv.Core.Configuration`: `EquivConfig`, `RenameMap`, `EquivConfigDiagnostic(s)`,
      `EquivConfigResult`, `EquivConfigParseException`, `EquivConfigLoader.Load(json)` — hand-rolled
      `JsonDocument` walk (no reflection, no new package), diagnostics for bad root/bound/timeout/
      rename entries/unknown properties, defaults substituted so callers can proceed after a warning.
- [x] `IVerificationBackend`/`VerificationOptions` stubs in `Equiv.Core` (ticket M3-001 finalises them).
- [x] tests: unit per diagnostic id and normalisation case; property test (`Equiv.Core.Tests`,
      inline CsCheck generator) that `StableIdentityMatcher.Match` is symmetric (swapping old/new
      swaps Added/Removed, keeps Pairs/Ambiguous) and total (every identity in old ∪ new lands in
      exactly one of Pairs/Added/Removed/Ambiguous) over generated identity multisets.

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
- Decision: `ProcedureIdentity` stays the M1-002 placeholder shape (`sealed record ProcedureIdentity(string
  Value)`), unchanged. Normalisation is a pure builder (`ProcedureIdentityNormalizer`) that *produces* the
  canonical `Value` string from raw namespace/type/member/arity/parameters (or verb/route) plus the rename
  map; it is not a change to the record itself. Alternatives: replace `ProcedureIdentity` with a closed
  `Member`/`Endpoint` union, as the M1-002 placeholder comment ("M1-003 replaces it") suggested. Rejected:
  `IrText`/`IrTextParser` and `IrGenLowering` already treat `Identity.Value` as an opaque display string
  (one existing test even round-trips an arbitrary string with quotes/backslashes/newlines through it to
  exercise the dump format's escaping), and matching only ever needs value-equality on the final string, so
  a union bought no capability here at the cost of reopening already-merged, snapshot-pinned M1-002 code.
  Rule: 4 (smaller change), 1 (mirrors the existing `IrText` consumer).
- Decision: new types live in `Equiv.Core.Verdicts`, `Equiv.Core.Matching`, `Equiv.Core.Configuration`
  (folder-per-namespace, mirroring `Equiv.Core.Ir`), not a flat `Equiv.Core`. Alternatives: flat namespace
  (matches `ProcedureIdentity`/`CallIdentity`/`SourceSpan`, which predate this ticket and stay where they
  are since `Equiv.Core.Ir` needs ancestor-namespace visibility into them). Rule: 4.
- Decision: `Counterexample(IrInputs Inputs, IrRun Old, IrRun New)`, reusing `IrRun` (outcome + outs +
  trace already bundled) instead of the four separate `oldOutcome/newOutcome/oldTrace/newTrace` fields
  M3-001's prose lists. Rule: 2 (closed/existing type over ad hoc field duplication).
- Decision: `UnknownReason` is a closed enum (`Timeout`, `Opaque`, `UnmatchedOverload`) — the three named
  in VERIFICATION-MODEL.md section 6's EQ003 row — plus a free-form `Detail` string for the solver
  message / opaque span / candidate list. Later milestones (loop ladder, CHC) add rungs, not reasons, so
  this can grow by adding enum members in those tickets. Rule: 2.
- Decision: `Added`/`Removed` are no-argument marker records; the identity of an unmatched procedure comes
  from `MatchResult.Added`/`.Removed`, not embedded in the verdict. Mirrors ARCHITECTURE.md's literal
  "Added, Removed. Nothing else." Rule: 1.
- Decision: `IProcedureMatcher.Match` takes `IReadOnlyCollection<ProcedureIdentity>` directly (not generic
  over a symbol/procedure type). Core has no frontend-agnostic "procedure" abstraction yet, and the
  ticket's own property-test wording ("generated identity sets") confirms identities are the right level;
  a future frontend maps matched identities back to its own symbols by a trivial dictionary lookup. Rule: 4.
- Decision: `ProcedurePair(ProcedureIdentity Old, ProcedureIdentity New)` — both fields always equal today
  (matching is by exact identity), kept as two fields to mirror ARCHITECTURE.md's literal "ProcedurePairs
  (old, new)". Reused as-is for the `IVerificationBackend` stub; M3-001 may need a different pair shape
  once lowering has a place in the pipeline (a verifier needs `IrProcedure` bodies, not just identities) —
  flagged in that type's doc comment. Rule: 1.
- Decision: ambiguity and totality in `StableIdentityMatcher`/`MatchResult` are defined over the *set* of
  distinct identity values in old ∪ new (an identity seen more than once on a side that also has it is one
  `Ambiguous` entry, not one per duplicate), matching the ticket's own "generated identity sets" phrasing.
  `MatchResult`'s order follows first appearance across old-then-new (not dictionary iteration order) for
  determinism, per the precedent in M1-002 Notes ("dump ordering follows block order, not a hash set").
  Rule: 3.
- Decision: `EquivConfigLoader.Load` walks `System.Text.Json.JsonDocument` by hand rather than
  `JsonSerializer.Deserialize<T>()`. Reflection-free deserialization needs the source generator
  (`JsonSerializerContext`), which is more machinery than five optional fields need, and a hand-rolled walk
  gives each problem its own message/JSON-pointer path instead of one generic exception. Mirrors the
  `IrText.Parse` (throws `EquivConfigParseException` on non-JSON, matching `IrParseException`) /
  `IrValidator.Validate` (returns diagnostics, never throws, matching `IrDiagnostic`/`IrDiagnosticIds`)
  split already established by M1-002. Rule: 1.
- Decision: `ImmutableDictionary`/`ImmutableArray`-bearing records (`RenameMap`, `EquivConfig`,
  `EquivConfigResult`, `MatchResult`, `VerificationOptions`) override `Equals`/`GetHashCode` for structural
  equality, combining field comparisons with non-short-circuit `&` (one branch instead of two per field),
  exactly the pattern `Equiv.Core.Ir.IrEquality`'s file comment documents. `MatchResult`/`EquivConfigResult`
  reuse `IrEquality.SequenceEqual`/`.Hash` directly (it is `internal`, hence assembly-visible) rather than
  duplicating it; a new `Equiv.Core.Configuration.ConfigEquality` covers the dictionary case `IrEquality`
  doesn't have. Rule: 1, 4.
- Toolchain: Meziantou's MA0002 fires on `ToImmutableDictionary`/LINQ calls over `string` keys without an
  explicit comparer, and CA1508 proves `x.Equals(null)` dead when the argument is a literal or a
  never-reassigned local — both only show up under `-warnaserror`, not a plain build. Fixed with
  `StringComparer.Ordinal` overloads and a `Null.Of<T>()` test helper (`tests/Equiv.Core.Tests/Null.cs`)
  that returns null through an opaque generic call so the strongly-typed `Equals(T?)` null branch stays
  reachable and testable without the analyzer folding it away.
