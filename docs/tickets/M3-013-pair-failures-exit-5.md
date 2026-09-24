# M3-013 A pair whose verification throws is reported and skipped, and the run exits 5
Status: in-progress
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-001

## Goal
ADR 0023. Today `CompareCommand.Verify` rethrows any backend exception naming the pair, so one
crashing pair (a replay mismatch M3-001 treats as an encoder bug, a `Z3Exception`) ends the whole
`compare` run. No SARIF is written, and System.CommandLine's default handler exits 1, the
"divergence" code. When this ticket is done, the failing pair is recorded as a SARIF tool-execution
notification and has no result. The other pairs are verified and reported. The run exits 5.
`Program.Main` maps any other unhandled exception to 5. The failed pair's baseline result is
carried through as `unchanged`, not `absent`. That is about one new exit code, one extra
`SarifReportWriter.Write` input, one baseline rule and one catch in `Program.Main`.

## Spec references
ADR 0023; ARCHITECTURE.md `Equiv.Cli` exit codes; VERIFICATION-MODEL.md section 6 (the
failed-pair paragraph and the baseline paragraph after it); ADR 0011 (why a crash is not
`Unknown`).

## Acceptance criteria (all must hold; nothing beyond them)
1. `ExitCodes.InternalError` is 5.
2. `CompareCommand` catches an exception from `IVerificationBackend.Verify` per pair, except
   `OperationCanceledException` and `OutOfMemoryException`, which propagate unchanged. The pair
   gets no `VerificationResult`, and every other pair is still verified.
3. `SarifReportWriter.Write` takes the pair failures as well as the results. Given at least one
   failure, the run has exactly one invocation with `executionSuccessful: false` and one
   `toolExecutionNotifications` entry per failure. Each entry has level `error`, a message naming
   both the legacy and the modern identity, and the exception in `exception`. The run's
   `properties.unverified` lists each failed pair's identity (the modern one, which the pair's
   result would have been keyed by). Given no failures, the SARIF output is byte-identical to
   today's: the existing `SarifReportWriterTests` snapshots do not change.
4. For an identity in the failures, `BaselineComputer` does not emit an `absent` result.
   If the baseline has a result for that identity, it is copied through with
   `baselineState: unchanged` and `properties.unverified: true`.
5. When any pair failed, `compare` still writes the SARIF log to the sink, then exits 5. This
   holds even when a new `Divergent` result or (with `--fail-on unknown`) a new `Unknown` result
   is present. The error message naming the pair is written to stderr.
6. `Program.Main` maps any exception that escapes the command (for example, the missing-body
   `InvalidOperationException` in `BuildResults`) to exit 5, with the message on stderr. It no
   longer returns System.CommandLine's exit 1 for it. Pass an `InvocationConfiguration` with
   `EnableDefaultExceptionHandler = false`, or catch around the command action. Decide which
   with `equiv-decide`.
7. README's exit-code table gains the row for 5.

## Files
- `src/Equiv.Cli/ExitCodes.cs`
- `src/Equiv.Cli/CompareCommand.cs`
- `src/Equiv.Cli/Program.cs`
- `src/Equiv.Core/Reporting/SarifReportWriter.cs`
- `src/Equiv.Core/Reporting/BaselineComputer.cs`
- One new `src/Equiv.Core/Reporting/` record for a pair failure (both identities and the
  exception), if `VerificationResult` cannot carry it
- `tests/Equiv.Cli.Tests/CompareCommandTests.cs`, `ProgramTests.cs`, `FakeBackend.cs` (a
  throwing verdict for one identity)
- `tests/Equiv.Core.Tests/Reporting/SarifReportWriterTests.cs` (plus one new `.verified.txt`),
  `BaselineComputerTests.cs`
- `README.md`

## Tests
- `Compare_PairThatThrows_IsReportedAsNotificationAndOtherPairsVerified`: two pairs, one throws.
  The SARIF log has the other pair's result, one error notification, `executionSuccessful: false`,
  and `properties.unverified`. Exit 5.
- `Compare_PairThatThrows_OutranksNewDivergent`: exit 5, not 1.
- `Compare_PairThatThrows_OutranksNewUnknownWithFailOnUnknown`: exit 5, not 2.
- `Compare_OperationCanceled_Propagates` and `Compare_OutOfMemory_Propagates`.
- `Compare_PairThatThrows_KeepsBaselineDivergentAsUnchanged`: the baseline holds a Divergent for
  the failing pair. The output has it as `unchanged` with `properties.unverified: true`, and
  no `absent` result.
- `Main_UnhandledException_Exits5`: stderr carries the message.
- `SarifReportWriterTests.PairFailure` snapshot.
- `BaselineComputerTests`: an unverified identity with a baseline result is copied as
  `unchanged`, and one without a baseline result adds nothing.

## Size guard
Twelve files, about ten new tests. If you are changing `IVerificationBackend`, `Verdict`, or
`UnknownReason`, you have drifted. ADR 0023 rejected modelling a crash as a verdict.

## Out of scope
- Catching lowering failures per pair. Lowering runs inside `ILanguageFrontend.Analyze`, so a
  lowering exception reaches criterion 6's process-level catch, not criterion 2.
- Turning `IrLowerer`'s `Debug.Assert(IrValidator.Validate(...))` into a Release-mode throw.
  ADR 0023 names this as a later step.
- Retrying a failed pair, or a flag to make a failure non-fatal.
- `action.yml` and Docker documentation of exit 5 (M3-004 picks up ARCHITECTURE.md's list).

## Notes
Pitfall: `DecideExitCode` pairs `results[i]` with `log.Runs[0].Results[i]` by index. Carried-through
unverified baseline results must go after the current results, as `absent` results already do,
or the pairing breaks.

Sarif SDK API (`Notification`, `ExceptionData`, `Invocation.ToolExecutionNotifications`,
`Invocation.ExecutionSuccessful`): read `Sarif.xml` using the `equiv-package-api` skill.

## Notes

Decision: `SarifReportWriter.Write` and `BaselineComputer` already took `notifications`/`unverified`
generically (ADR 0029, M3-024's project-skip path); neither needed a code change, and neither needed
a new pair-failure-specific test — the generic ones (`AnErrorNotificationMakesTheInvocationUnsuccessful`,
`UnverifiedIdentitiesAreListedOnceEachOnTheRun`, `ABaselineResultInASkippedProjectIsCarriedUnchanged`,
`AnUnverifiedIdentityWithoutABaselineResultAddsNoResult`) already cover criteria 3 and 4 for any
unverified identity, pair-failure or project-skip alike. `CompareCommand` only had to build a
`Notification`/`ExceptionData` per failed pair and route it through the same two parameters, so no new
`src/Equiv.Core/Reporting/` record was needed either.

Decision: `Program.Main`'s pipeline is now `Program.Run(args, frontends, backend)`, an internal seam
(the pattern every other frontend/loader in this repo already uses) so `Main_UnhandledException_Exits5`
can inject a `FakeFrontend` that reproduces the missing-body `InvalidOperationException` criterion 6
names, without needing a real MSBuild/Z3 crash.

Decision: skipped the `SarifReportWriterTests.PairFailure` Verify-snapshot the ticket's Tests section
names. This dev environment has no `dotnet` (network policy blocks `builds.dotnet.microsoft.com`, see
below), so a hand-authored `.verified.txt` could not be checked against the Sarif SDK's actual
serialization and would be a guess. `SarifReportWriter.Write` doesn't build the `Exception` shape
itself (that's `CompareCommand`'s job, per the decision above) and already passes a `Notification`
through unchanged (ADR 0029), so `APairFailureNotificationCarriesItsExceptionThroughToTheInvocation`
(a plain assertion, not a snapshot) covers the one thing that actually changed at that layer: the
`Exception` property surviving the round trip. If a snapshot is still wanted, it needs a `dotnet test`
run to generate and approve the `.verified.txt`.

Toolchain: this session's container has no `.NET` SDK installed, and installing one is blocked by the
environment's network policy (`curl https://dot.net/v1/dotnet-install.sh` and
`builds.dotnet.microsoft.com` both get a 403 from the egress proxy). No `dotnet build`/`dotnet test`
could be run locally for this ticket; the code and tests were written against the exact patterns and
Sarif SDK API already used elsewhere in this repo (`Sarif.xml` for `sarif.sdk` 5.7.0, and the
`System.CommandLine` 2.0.12 source for `InvocationConfiguration`, both confirmed by fetching the
package/source directly), and CI is the first real build/test run.

Deviation: developed on the harness-assigned branch (`claude/magical-pasteur-3whfup`) rather than a new
`m3-013-...` branch off `main`, per this session's Git Development Branch Requirements, which override
the skill's own branch step for this environment.
