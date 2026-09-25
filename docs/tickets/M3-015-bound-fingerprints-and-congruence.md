# M3-015 Bound fingerprints, congruence without the solver, and the callee pairs every verdict assumes
Status: in-progress
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

- Decision: `BodyFingerprint` lives in `Equiv.Core.Matching` beside `ProcedurePair`, since Core cannot see the frontend.
  `ProofMethod` gains `Congruence`, the value criterion 5 puts in `properties.proofMethod`.
- Decision: the serialisation (`Fingerprinting/BoundSerialiser`, one `OperationWalker`, about 280 lines) writes one line
  per operation: its kind, syntax kind, `IsImplicit`, type, constant, the symbols it references, and its `checked`
  context. `OperationKind.None` and `dynamic` operations also carry their source tokens, because their meaning is not in
  their kind and symbols. The cost is congruence on a local rename inside them, never a false equality.
- Decision: a property reference is spelled as its accessors' call identities, so `runtime-changes.json` rows that name
  an accessor (`System.Text.Encoding::get_Default(`) match it. Fields and events are `Type::Name`, matched by prefix.
- Decision: `suppressRuntimeChanges` is honoured by the runtime-sensitive flag, as it is by the IR's `RuntimeChanged`.
- Decision: "floating-point arithmetic on x87" is any operation of type `float` or `double`, when the legacy
  compilation's platform is `X86` or `AnyCpu32BitPreferred` (MSBuild's `PlatformTarget=x86`, and `AnyCPU` with
  `Prefer32Bit`). That reads the compilation options, so no loader change was needed. A float-to-integer conversion
  counts when its target is a nullable or an enum over an integral type too.
- Decision: an interpolated string that is not a handler argument records how the compiler lowers it:
  `DefaultInterpolatedStringHandler` when the language version is 10 or later and the type exists, else `string.Format`.
- Decision: API-equivalence type entries rename types. A member entry renames the callee only when its adapter passes
  every argument through unchanged. Any other adapter changes the arguments, which a fingerprint of the tree as bound
  cannot express, so those calls keep their legacy name and the pair goes to the solver.
- Decision: an auto-accessor (a property with a compiler-generated backing field) is fingerprinted as the body the
  compiler generates for it, a header plus `AutoAccessor`. "No body" means `GetOperation` returns null and there is no
  backing field (a partial definition, an abstract accessor). Without this, criterion 8's 14 accessors would stay Unknown.
- Decision: a constructor that does not chain to `this(...)`, and a static constructor, serialise the field and property
  initializers of their staticness first, because those run as part of the constructor.
- Decision: the census's token-equality proxy (`SyntaxTokens`, `ProcedurePair.TokensEqual`, M3-030) is removed. A pair
  is changed unless it is congruent, as VERIFICATION-MODEL section 6 said would happen at this ticket.
- Decision: `CompareCommand.IsCongruent` is `internal` so the soundness property in `Equiv.Tests.Integration` checks
  the production rule. The property lives there because it needs both the frontend and Z3, and that project now
  references `Equiv.TestSupport` for `LoweringOracleGen`.
- Decision: a matched pair whose lowering threw (P2-011) is still a matched pair for criterion 9. It has no result, so it
  is always among `unprovenAssumptions`.
- Surprise: `business-layer`'s `Describe` is not congruent. Its interpolated string binds `string.Format` on 4.8 and
  `DefaultInterpolatedStringHandler` on .NET 10, which is exactly criterion 3's drift. No ticket lowers interpolated
  strings, and M4-004's fragment fingerprints will differ for the same reason. The README says so.
- Surprise: `OperationWalker` does not route `OperationKind.None` operations (`__makeref`) through `DefaultVisit`, so
  the serialiser overrides `Visit` and recurses over `ChildOperations` itself.
- Surprise: an expression-bodied member and the same logic in a block body are not congruent, because the implicit
  `return` differs. That costs precision only.
- Surprise: a call to a generic method has a callee identity with constructed parameter types and `<typeArgs>`, so it
  never equals a matched pair's identity and is missing from `assumedCallees`. That comes from the existing identity
  scheme and is left alone here.
- Surprise: `CongruenceSampleTests` over `webapi-basic` needs the samples restored, as every sample test does
  (`build.ps1 -Integration` does it).
