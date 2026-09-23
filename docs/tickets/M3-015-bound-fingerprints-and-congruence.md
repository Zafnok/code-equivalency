# M3-015 Bound fingerprints, congruence without the solver, and the callee pairs every verdict assumes
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-014, M3-001, M3-009, M3-024

## Goal
Most bodies in a migration, and on any PR, are unchanged. After this ticket a matched pair whose
bound bodies fingerprint equal, and neither of which is runtime-sensitive, is Equivalent with
`proofMethod: congruence`, whatever it contains, and without calling Z3 (ADR 0024 decision 1).

A congruence verdict is modular: it assumes the callees are equivalent. So is every solver verdict.
So this ticket also makes every result name the matched callee pairs it assumed equivalent, and
flag any of them that this run did not prove (ADR 0019). A caller whose callee changed is then no
longer reported as a bare Equivalent. No verdict and no exit code changes because of the
assumptions. (This ticket absorbed M3-008 in the 2026-09-21 consolidation. Landing it before
M3-003 means the sample snapshots are approved once, with the properties.)

## Spec references
ADR 0024; ADR 0019; ADR 0020 (catalogue rewrites precede fingerprinting); ADR 0021 (parameters by
position); VERIFICATION-MODEL sections 1, 3, 4 and 6.

## Design
Two commits: fingerprints and the congruence short-cut (criteria 1 to 8), then the assumption
lists (criteria 9 to 14), which apply to congruence and solver results alike.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Equiv.Frontend.CSharp` computes a `BodyFingerprint(string Sha256Hex, bool RuntimeSensitive)`
   per body from the canonical serialisation ADR 0024 defines. `ProcedurePair` carries
   `OldFingerprint` and `NewFingerprint`, both nullable. They are null when there is no body.
2. The serialisation is its own internal class with a readable text form. Tests compare that text,
   not only the hash.
3. Local names, comments, formatting and parameter names do not change the fingerprint. A different
   overload, operator, conversion, constant or `checked` context does. So does
   `DefaultInterpolatedStringHandler` binding against `string.Format` binding.
4. The fingerprint is runtime-sensitive when the body references any `runtime-changes.json` member,
   contains a floating-point to integer conversion, or contains floating-point arithmetic while the
   legacy project's effective platform is x86 (`PlatformTarget=x86`, or `AnyCPU` with
   `Prefer32Bit=true`).
5. In `CompareCommand`, a pair with equal, non-runtime-sensitive fingerprints produces
   `Equivalent` with `proofMethod: congruence`, and the backend is not called. Every other pair
   goes to the backend as before. A body that M3-024 marks unbound (a compiler error, an
   `IInvalidOperation` or an error-type symbol) is never congruent (ADR 0029 decision 2): two error
   symbols with the same name are no evidence of the same behaviour.
6. `loweringCensus.pairsCongruent` is filled in.
7. **Soundness property.** Over `LoweringOracleGen` programs, and over every sample pair, when
   congruence says Equivalent, the Z3 backend on the same pair never says Divergent.
8. On `business-layer`, every unchanged method that is not runtime-sensitive is Equivalent by
   congruence. Its README and census snapshot are updated.
9. After all verdicts in a run are computed, `CompareCommand` computes, for each result whose
   `ProcedurePair` has both bodies, `assumedCallees`: the distinct `IrCall.Callee` values in
   either body that equal the normalised identity of some matched pair in the same
   `MatchResult`, sorted ordinally. A call to the procedure itself (recursion) is excluded.
10. `unprovenAssumptions` is the subset of `assumedCallees` whose own result in this run is not
    Equivalent.
11. The SARIF writer emits both lists as `properties.assumedCallees` and
    `properties.unprovenAssumptions` (string arrays), and omits each one when it is empty.
12. An Equivalent result with a non-empty `unprovenAssumptions` appends one sentence to its
    message: `Assumes callees equivalent; not proved for: <comma-separated identities>.`
13. New sample `callee-changed`: `Total(int a) => Tax(a) + a` is unchanged, and `Tax` changes its
    rate. Its README's verdict table says: `Tax` Divergent; `Total` Equivalent (by congruence),
    with `unprovenAssumptions` naming `Tax`; exit code 1. M3-003's end-to-end theory asserts it.
14. The result fingerprint (M1-004) is unchanged. The two lists are not part of it, so adding
    them does not make baseline results `updated`. Existing `SarifReportWriterTests` snapshots
    change only by the new properties and message sentences.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/*` (new), `src/Equiv.Core/Matching/ProcedurePair.cs`,
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/LoweringCensus.cs`,
`src/Equiv.Core/Reporting/SarifReportWriter.cs`, `src/Equiv.Core/Verdicts/*` (only if the
assumptions must travel on `VerificationResult`), `samples/business-layer/README.md`,
`samples/callee-changed/**`, tests in the matching test projects.

## Tests
- `LocalRenameKeepsTheFingerprint`, `CommentsAndFormattingKeepTheFingerprint`,
  `ParameterRenameKeepsTheFingerprint`, `OverloadDriftChangesTheFingerprint`,
  `InterpolatedStringHandlerChangesTheFingerprint`, `CheckedContextChangesTheFingerprint`,
  `RuntimeChangedMemberIsRuntimeSensitive`, `FloatToIntConversionIsRuntimeSensitive`,
  `X87LegacyFloatIsRuntimeSensitive`, `CongruentPairSkipsTheBackend`, `AnUnboundMethodIsNeverCongruent`,
  `CongruenceNeverContradictsTheSolver` (property), and snapshots of three canonical serialisations.
- `Assumptions_ListMatchedCalleesOnly`, `Assumptions_ExcludeSelfRecursion`,
  `UnprovenAssumptions_AreCalleesNotEquivalent`, `EquivalentWithUnprovenAssumption_MessageNamesThem`,
  `Assumptions_DoNotChangeTheFingerprint`, `CongruentResult_ListsAssumedCallees`.

## Size guard
No `Equiv.Core` change beyond `ProcedurePair`, the SARIF writer's two properties and message
sentence, and `VerificationResult` if criterion 9 needs it. No change to `Equiv.Verify.Z3`. If the
serialiser needs more than about 300 lines, stop: it must be one `OperationWalker`, not a per-kind
printer.

## Out of scope
Fragment fingerprints on `IrOpaque` (M4-004). Caching verdicts across runs. Changing any verdict
based on callee results. Transitive assumptions (a callee's callees). Call graph visualisation.

## Notes
