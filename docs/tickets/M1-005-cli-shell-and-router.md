# M1-005 CLI shell and router
Status: in-progress
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
- Decision: `CompareCommand.Run` takes an `IReportSink sink` parameter instead of building a
  `FileReportSink` internally from `--out`. `Create` (the System.CommandLine wiring) constructs the
  production `FileReportSink(outPath)`; tests pass `InMemoryReportSink` so the pipeline is
  verifiable without touching disk, per the ticket's own "fakes live here" test list. Rule: 3 (the
  fake-based test list is unwritable otherwise).
- Decision: the exit-code check (`--fail-on`/new-Divergent/new-Unknown) reads the just-written
  `SarifLog`'s `BaselineState` and `RuleId` per result, rather than re-deriving new/updated/absent
  itself. `Equiv.Core.Reporting.BaselineComputer` (the thing that actually knows this) is
  `internal` to `Equiv.Core`, with `InternalsVisibleTo` only for `Equiv.Core.Tests`; duplicating its
  identity+rule-id matching in `Equiv.Cli` would both violate CLAUDE.md's "Equiv.Core is the
  contract" boundary and drift from M1-004's baseline notes on `new` vs `updated`. Reusing the
  `SarifLog` `Equiv.Cli` already writes (matching `VerificationResult`s 1:1 by index, since
  `SarifReportWriter.Write` appends absent carry-overs after them) needed no new public API. Rule: 1
  (mirrors M1-004's own documented new/updated semantics without a second implementation of them).
- Decision: `VerificationResult`'s `Identity` for a matched pair uses `ProcedurePair.New` (not
  `Old`). `ProcedurePair`'s own doc comment guarantees `Old.Value == New.Value`, so this is
  cosmetic; picked `New` since a report is read against the modern side. Rule: 1 is silent here, so
  Rule 4 (arbitrary but documented, in case a later ticket cares which record instance survives).
- Decision: `Program.Main` checks `parseResult.Errors.Count > 0` itself and writes each error
  `Message` to stderr, instead of calling System.CommandLine's default `parseResult.Invoke()` path
  for a parse failure. Measured: the default `ParseErrorAction` prints the error *and* a full
  `Usage:`/`Options:` help block to **stdout**, which would violate acceptance criterion 8
  ("nothing to stdout except the dry-run line"); its default exit code is also `1`, not this
  ticket's `3`. Verified against the real `System.CommandLine` 2.0.12 package (a throwaway console
  probe outside the repo) before writing this, not assumed. Rule: 1 (acceptance criterion 8 is
  explicit) and 4 (the manual check is smaller than fighting the library's default help renderer).
- Decision: `FrontendRouter.Route` returns a single `ILanguageFrontend?` and does not distinguish
  "no frontend supports either path" from "two different frontends each support only one side" —
  both are `null`, and `CompareCommand.Run` emits one generic stderr line for either. The ticket's
  acceptance criteria ask for "exit 3 with one line on stderr naming the paths" for both cases, not
  two different messages. Rule: 1.
- Decision: `Matching.MatchResult.Ambiguous` is not read anywhere in `CompareCommand.Run`. The
  ticket's acceptance criteria (point 5) name only `Pairs`, `Added`, and `Removed` becoming
  `VerificationResult`s; M1-004's Notes left `Ambiguous -> Unknown(UnmatchedOverload)` wiring
  unassigned to "whichever ticket first produces SARIF results from a MatchResult (M1-004 or
  M1-005)" and M1-005's own Goal/Acceptance criteria never mention it. Still unassigned after this
  ticket — flagging again per M1-004's own "Second review pass" follow-up note, for whichever
  ticket next touches this pipeline. Rule: 1 (ticket text is explicit about which three fields).
- Decision (`equiv-adr`-adjacent, resolved without one): CA1707 ("remove underscores from member
  names") flagged the ticket's own literal test names (`Compare_Exits1OnDivergent`, etc.), an
  idiomatic xUnit `Scenario_Outcome` convention this repo had not needed before. Lowered to `none`
  for `tests/**.cs` only in `.editorconfig`, per CLAUDE.md's own escape hatch ("use `.editorconfig`
  severity with a comment if a rule is genuinely wrong for this repo, and mention it in the PR").
  Not an architecture change, so no ADR: it only affects test-identifier style, not build/runtime
  behaviour.
- Toolchain: verified `System.CommandLine` 2.0.12's actual API (not assumed from memory) via a
  disposable console app outside the repo before writing `CompareCommand.Create`/`Program.Main`:
  `Option<T>.Required`, `Option<T>.GetValue` returning `T?` even for a required option (hence the
  `!` null-forgiving reads after confirming `parseResult.Errors.Count == 0`), and the stdout/exit-
  code behaviour noted above. Took under 15 minutes; no gate or library behaviour needed reporting
  as "wrong".
