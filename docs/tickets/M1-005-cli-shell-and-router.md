# M1-005 CLI shell and router
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M1-004

## Goal
`equiv compare` parses its options, picks a frontend by file extension, runs the
pipeline (frontend, backend per pair, SARIF writer, sink) and exits with the documented
code. No frontend or backend exists yet, so the shipped binary rejects every real input
with exit 3, and the pipeline is proven with fakes in tests. This is plumbing: about six
small files and a dozen tests.

## Spec references
ARCHITECTURE.md "Equiv.Cli" (options and exit codes); VERIFICATION-MODEL.md section 6
(exit code counts only `new` baseline results).

## Acceptance criteria (all must hold; nothing beyond them)
1. `Equiv.Core` gains `ILanguageFrontend` with exactly:
   `string Language { get; }`, `bool Supports(string path)`,
   `MatchResult Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct)`.
   Plus `FrontendLoadException(string path, string detail)`. Nothing else in Core changes.
2. `equiv compare --legacy <path> --modern <path> [--out <file>] [--baseline <file>]
   [--config <file>] [--fail-on divergent|unknown] [--dry-run]` parses with
   System.CommandLine 2.0. `--out` defaults to `equiv.sarif`. `--fail-on` defaults to
   `divergent`. `--bound` and `--timeout-ms` are NOT options; they come from the config file.
3. Routing: a `FrontendRouter` picks the single frontend whose `Supports` is true for
   both paths. Both false, or different frontends: exit 3 with one line on stderr naming
   the paths. Missing file: exit 3. Unknown option or missing required option: exit 3
   (System.CommandLine's own message is fine).
4. `--dry-run` prints exactly one line to stdout,
   `route: <language> legacy=<path> modern=<path> out=<file>`, and exits 0 without
   calling `Analyze`.
5. Pipeline (`CompareCommand.Run`): load config (`EquivConfigLoader.Load` on the file, or
   `EquivConfig.Default` when `--config` is absent); `Analyze`; for each `ProcedurePair`
   call `IVerificationBackend.Verify` with `VerificationOptions(config.Bound,
   config.TimeoutMs, config.CallIdentityRenames)`; `Added`/`Removed` from the
   `MatchResult` become `VerificationResult`s with the matching verdicts; `SarifReportWriter.Write`
   with the baseline log when `--baseline` is given; `IReportSink.Write`.
6. Exit code: 4 if `Analyze` throws `FrontendLoadException` (message on stderr);
   1 if any result with `baselineState` `new` (or all results when no baseline) is
   Divergent; 2 if none is Divergent, `--fail-on unknown`, and any is Unknown; else 0.
7. `Program.Main` composes `CompareCommand` with an empty frontend list and a backend
   that is never reached (a private `NoBackend` whose `Verify` throws
   `InvalidOperationException`; it is unreachable because no frontend exists, and it is
   covered by a direct unit test). No DI container, no new NuGet package.
8. Nothing is written to stdout except the dry-run line; diagnostics go to stderr.

## Files
`src/Equiv.Core/ILanguageFrontend.cs`, `src/Equiv.Core/FrontendLoadException.cs`,
`src/Equiv.Cli/Program.cs`, `src/Equiv.Cli/CompareCommand.cs` (option definitions and
`Run`), `src/Equiv.Cli/FrontendRouter.cs`, `src/Equiv.Cli/ExitCodes.cs` (constants
0 to 4), `src/Equiv.Cli/NoBackend.cs`.

## Tests (`Equiv.Cli.Tests`, fakes live here)
`FakeFrontend` (configurable `Supports`, canned `MatchResult` or throws) and
`FakeBackend` (verdict per identity), `InMemoryReportSink`. Test names:
`Router_PicksFrontendSupportingBothPaths`, `Router_RejectsWhenNoFrontend` (exit 3),
`Router_RejectsWhenFrontendsDiffer` (exit 3), `Compare_MissingFileExits3`,
`Compare_DryRunPrintsRouteAndExits0`, `Compare_WritesSarifAndExits0WhenAllEquivalent`,
`Compare_Exits1OnDivergent`, `Compare_Exits2OnUnknownWhenFailOnUnknown`,
`Compare_Exits0OnUnknownByDefault`, `Compare_Exits4OnFrontendLoadException`,
`Compare_BaselineSuppressesUnchangedDivergent`, `Compare_UsesConfigBoundAndTimeout`,
`Main_WithNoArgsExits3`, `NoBackend_Throws`. Snapshot (Verify) of the SARIF from
`Compare_WritesSarifAndExits0WhenAllEquivalent` with two Equivalent, one Added, one Removed.

## Size guard
If your plan has more than 8 files in `src/` or more than 20 tests, you have misread
the ticket. Re-read "Out of scope".

## Out of scope
Any Roslyn or Z3 code. Content sniffing of files. Stub frontends in `src/`. Logging
frameworks. Progress output. `equiv.config.json` schema changes.

## Notes
