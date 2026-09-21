# M3-008 Name the callee pairs every verdict assumes
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-003

## Goal
A caller whose callee changed is no longer reported as a bare Equivalent. Every result for a
matched pair with bodies lists the matched callee pairs its verdict assumed equivalent, and flags
any of them that this run did not prove equivalent (ADR 0019). No verdict and no exit code
changes.

## Spec references
VERIFICATION-MODEL.md sections 1 and 6; ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. After all verdicts in a run are computed, `CompareCommand` computes, for each result whose
   `ProcedurePair` has both bodies, `assumedCallees`: the distinct `IrCall.Callee` values in
   either body that equal the normalised identity of some matched pair in the same
   `MatchResult`, sorted ordinally. A call to the procedure itself (recursion) is excluded.
2. `unprovenAssumptions` is the subset of `assumedCallees` whose own result in this run is not
   Equivalent.
3. The SARIF writer emits both lists as `properties.assumedCallees` and
   `properties.unprovenAssumptions` (string arrays), and omits each one when it is empty.
4. An Equivalent result with a non-empty `unprovenAssumptions` appends one sentence to its
   message: `Assumes callees equivalent; not proved for: <comma-separated identities>.`
5. New sample `callee-changed`: `Total(int a) => Tax(a) + a` is unchanged, and `Tax` changes its
   rate. Expected: `Tax` Divergent; `Total` Equivalent, with `unprovenAssumptions` naming `Tax`;
   exit code 1. It has a README with the verdict table, and M3-003's end-to-end theory picks it
   up automatically.
6. Every sample's `expected.sarif.json` snapshot is re-approved, and the only diffs are the new
   properties and message sentences.
7. The result fingerprint (M1-004) is unchanged. The two lists are not part of it, so adding
   them does not make baseline results `updated`.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Core/Reporting/SarifReportWriter.cs`,
`src/Equiv.Core/Verdicts/*` (only if the assumptions must travel on `VerificationResult`),
`samples/callee-changed/**`, snapshot re-approvals, and their tests.

## Tests
`Assumptions_ListMatchedCalleesOnly`, `Assumptions_ExcludeSelfRecursion`,
`UnprovenAssumptions_AreCalleesNotEquivalent`, `EquivalentWithUnprovenAssumption_MessageNamesThem`,
`Assumptions_DoNotChangeTheFingerprint`, plus the `callee-changed` end-to-end assertion.

## Size guard
No change to `Equiv.Verify.Z3` or the frontend beyond what the sample needs. More than three `src/`
files touched means you have misread the ticket.

## Out of scope
Changing any verdict based on callee results. Transitive assumptions (a callee's callees). Call
graph visualisation.

## Notes
