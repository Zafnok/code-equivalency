# M1-004 sarif and baseline
Status: in-progress
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-003

## Goal
Sarif.Sdk writer producing the section 6 mapping with rule metadata for EQ001 to EQ005; result fingerprint and baselineState computation; IReportSink with a file implementation. Snapshot tests for each verdict kind; a test that validates output with the SARIF SDK validator; property test: baselining a report against itself yields all unchanged.

## Spec references
ARCHITECTURE.md and VERIFICATION-MODEL.md; the implementer lists exact sections here before coding.

## Deliverables
- [ ] `Equiv.Core.Reporting.VerificationResult(ProcedureIdentity Identity, Verdict Verdict)`: one
      row for the report (the hand-off point between matching/verification and reporting; wiring
      a full `MatchResult` + `IVerificationBackend` run into a list of these is CLI-orchestration,
      out of scope here).
- [ ] `Equiv.Core.Reporting.VerdictRule`: internal mapping `Verdict` -> (ruleId, `FailureLevel`,
      `ResultKind`, message) per VERIFICATION-MODEL.md section 6, EQ001-EQ005 only (EQ006 excluded
      per the ticket goal).
- [ ] `Equiv.Core.Reporting.ResultFingerprint`: internal `Compute(VerificationResult)` -> stable
      hex string from procedure identity + verdict kind + verdict payload ("model hash").
- [ ] `Equiv.Core.Reporting.BaselineComputer`: internal, computes each current result's
      `BaselineState` (new/unchanged/updated) against a previous `SarifLog`, plus absent entries
      for previous results whose identity no longer appears.
- [ ] `Equiv.Core.Reporting.SarifReportWriter`: public, `Write(IReadOnlyList<VerificationResult>,
      SarifLog? baseline)` -> `SarifLog` with `Tool.Driver.Rules` = EQ001..EQ005 metadata, one
      `Result` per input row (plus absent baseline carry-overs), SARIF 2.1.0 schema/version.
- [ ] `Equiv.Core.IReportSink` (root namespace, alongside `IVerificationBackend`): `Write(SarifLog)`.
- [ ] `Equiv.Core.Reporting.FileReportSink(string Path) : IReportSink`: writes the log to a file
      via `SarifLog.Save`.
- [ ] `src/Equiv.Core/Equiv.Core.csproj`: add `Sarif.Sdk` `PackageReference` (already an ADR 0002
      row; no ADR change needed). `tests/Equiv.Core.Tests/Equiv.Core.Tests.csproj` too, for tests.
- [ ] tests (`tests/Equiv.Core.Tests/Reporting/`):
  - `VerdictRuleTests.cs`: one test per row named after it (`Equivalent_IsEQ001`,
    `Divergent_IsEQ002`, `Unknown_IsEQ003`, `Added_IsEQ004`, `Removed_IsEQ005`).
  - `ResultFingerprintTests.cs`: same (identity, verdict) -> same fingerprint; different identity,
    different verdict kind, or different payload (counterexample/reason) -> different fingerprint.
  - `SarifReportWriterTests.cs`: one Verify snapshot per verdict kind (Equivalent, Divergent,
    Unknown, Added, Removed); a round-trip test (`SarifLog.Save`/`Load` via the SDK) asserting the
    written log parses back equal and declares SARIF 2.1.0 (schema URI + version) — the "SDK
    validator" obligation, no hand-rolled JSON Schema.
  - `BaselineComputerTests.cs`: unit cases for new/unchanged/updated/absent; CsCheck property test
    `BaselineAgainstItself_IsAllUnchanged` (generated result sets, baseline against their own
    output -> every result's `BaselineState` is `Unchanged`, no absent entries).
  - `FileReportSinkTests.cs`: writes to a temp path, asserts the file round-trips via `SarifLog.Load`.

## Out of scope
Anything not in the goal. Stub the next ticket's interface; do not implement it.

## Notes
