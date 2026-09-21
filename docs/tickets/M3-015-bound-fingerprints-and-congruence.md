# M3-015 Bound fingerprints, and congruence verdicts without the solver
Status: todo (blocked on ADR 0024 acceptance)
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-014, M3-001, M3-009

## Goal
Most bodies in a migration, and on any PR, are unchanged. After this ticket a matched pair whose
bound bodies fingerprint equal, and neither of which is runtime-sensitive, is Equivalent with
`proofMethod: congruence`, whatever it contains, and without calling Z3 (ADR 0024 decision 1).

## Spec references
ADR 0024; ADR 0019 (assumedCallees); ADR 0020 (catalogue rewrites precede fingerprinting);
ADR 0021 (parameters by position); VERIFICATION-MODEL sections 3 and 4.

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
   `Equivalent` with `proofMethod: congruence` and `assumedCallees` as ADR 0019 defines, and the
   backend is not called. Every other pair goes to the backend as before.
6. `loweringCensus.pairsCongruent` is filled in.
7. **Soundness property.** Over `LoweringOracleGen` programs, and over every sample pair, when
   congruence says Equivalent, the Z3 backend on the same pair never says Divergent.
8. On `business-layer`, every unchanged method that is not runtime-sensitive is Equivalent by
   congruence. Its README and census snapshot are updated.

## Files
`src/Equiv.Frontend.CSharp/Fingerprinting/*` (new), `src/Equiv.Core/Matching/ProcedurePair.cs`,
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/LoweringCensus.cs`, tests in the matching test
projects, `samples/business-layer/README.md`.

## Tests
`LocalRenameKeepsTheFingerprint`, `CommentsAndFormattingKeepTheFingerprint`,
`ParameterRenameKeepsTheFingerprint`, `OverloadDriftChangesTheFingerprint`,
`InterpolatedStringHandlerChangesTheFingerprint`, `CheckedContextChangesTheFingerprint`,
`RuntimeChangedMemberIsRuntimeSensitive`, `FloatToIntConversionIsRuntimeSensitive`,
`X87LegacyFloatIsRuntimeSensitive`, `CongruentPairSkipsTheBackend`,
`CongruenceNeverContradictsTheSolver` (property), and snapshots of three canonical serialisations.

## Size guard
No `Equiv.Core` change beyond `ProcedurePair`. If the serialiser needs more than about 300 lines,
stop: it must be one `OperationWalker`, not a per-kind printer.

## Out of scope
Fragment fingerprints on `IrOpaque` (M3-017). Caching verdicts across runs.

## Notes
