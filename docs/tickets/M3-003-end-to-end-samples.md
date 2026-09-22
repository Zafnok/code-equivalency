# M3-003 End to end on all samples
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004, M2-005, M2-006, M3-002, M3-007, M3-009, M3-013, M3-014, M3-015, M3-016, P1-005, P1-006 (ADR 0018: no sample
verdicts ship with a known silent false Equivalent; ADRs 0024, 0026 and 0027: snapshots are taken
with congruence, taint and Unknown locations in place, so they are not rewritten three times)

## Goal
Wire the real backend into the CLI, run every sample through `equiv compare`, check in
the SARIF snapshots, and assert exit codes. This ticket writes almost no logic; it
connects what exists and pins the behaviour. It also owns `MatchResult.Ambiguous` ->
`Unknown(UnmatchedOverload)`, deferred by M1-003 ("M1-004 or M1-005") and again by
M1-004/M1-005 (neither's goal named it): this is the first ticket that assembles a real
`MatchResult` from `StableIdentityMatcher` into `VerificationResult`s end to end, so it
is the natural place to stop leaving ambiguous overloads out of the SARIF entirely.

## Spec references
ARCHITECTURE.md (exit codes, data flow); VERIFICATION-MODEL.md section 6; each
sample's README (expected verdicts).

## Acceptance criteria (all must hold; nothing beyond them)
1. `Program.Main` composes `CSharpFrontend`, `Z3Backend` (with `LoopLadder`), and
   `FileReportSink`. `NoBackend` from M1-005 is deleted.
2. `ILanguageFrontend.Analyze` returns lowered bodies: `ProcedurePair` gains
   `IrProcedure? OldBody` and `IrProcedure? NewBody` (null only when lowering failed,
   which yields `Unknown(UnknownReason.Opaque)` in the pipeline without calling the
   backend). If M2-003 already made this change, keep it.
3. For every directory under `samples/`, an integration test runs the CLI in-process
   (`Program.Main`) with a temp `--out`, then asserts: the exit code named in the
   sample README, and the SARIF matches the checked-in `expected.sarif.json` after
   normalising absolute paths to the sample-relative form and stripping
   `invocations[].startTimeUtc`/`endTimeUtc` and tool version. A test helper does the
   normalisation; snapshots are Verify files.
4. Expected verdicts per sample README are asserted explicitly, not only via snapshot:
   `identical` all Equivalent with `proofMethod` present; `renamed-locals` Equivalent;
   `added-branch` Divergent with a counterexample in the message; `removed-null-check`
   Divergent with `exceptionType` differing; `loop-bound-change` Divergent on rung 1;
   `webapi-basic` matched by endpoint and Equivalent, including `Find` with
   `properties.equivalencesApplied` (M3-009); `api-drift` `Parts` Equivalent and `HasX`
   Divergent on a null `s`; `added-removed` EQ004 and EQ005; `callee-changed` `Tax` Divergent and
   `Total` Equivalent with `properties.unprovenAssumptions` naming `Tax` (M3-015).
5. `--baseline` round trip: running a sample, then running it again with the first SARIF
   as baseline, exits 0 and every result has `baselineState: unchanged`.
6. README gains a "Usage" section with the exact command, the exit code table, and one
   real Divergent result excerpt copied from the `added-branch` output.
7. `CompareCommand`'s `MatchResult` -> `VerificationResult` assembly (M1-005) also handles
   `MatchResult.Ambiguous`: each identity becomes `Unknown(UnmatchedOverload, detail)`,
   same as any other `Unknown` result (subject to `--fail-on unknown`, SARIF EQ003).

## Files
`src/Equiv.Cli/Program.cs`, `src/Equiv.Cli/CompareCommand.cs` (criterion 7's `Ambiguous` wiring),
`src/Equiv.Core/Matching/ProcedurePair.cs` (if needed),
`tests/Equiv.Cli.Tests/CompareCommandTests.cs` (criterion 7's coverage),
`tests/Equiv.Tests.Integration/SamplesEndToEndTests.cs`, `SarifNormalizer.cs`,
`samples/*/expected.sarif.json` (Verify snapshots), `README.md`.

## Tests
One `[Theory]` over sample directories for criterion 3, one `[Fact]` per bullet in
criterion 4, `Baseline_RoundTripIsAllUnchanged`.

## Size guard
No new logic in `src/` beyond composition and the `ProcedurePair` fields. If a sample
does not produce its README verdict, do not fix the engine here: record it in Notes,
mark the assertion `Skip` with the reason, and file a ticket.

## Out of scope
Engine fixes. Packaging (M3-004). New samples.

## Notes
