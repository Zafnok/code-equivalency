# M1-005 CLI shell and router
Status: done (PR #22)
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
- Decision: `FrontendLoadException` gets the ticket's `(path, detail)` constructor plus the three
  standard exception constructors (parameterless, `(message)`, `(message, innerException)`) and the
  `Path`/`Detail` properties the `(path, detail)` overload needs to expose what it captured.
  Acceptance criterion 1 names only `FrontendLoadException(string path, string detail)`; the extra
  members are not a scope expansion but CA1032 ("implement standard exception constructors", on by
  default under `AnalysisLevel=latest-all`) forcing the same shape this repo's other two custom
  exceptions (`EquivConfigParseException`, `IrParseException`) already use — omitting them would
  fail the build gate, not just the analyzer suggestion. Rule: 1 (mirrors the two existing
  exceptions in this codebase) and 4 (the smallest change that keeps the build green).
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

## Review response (post-PR review, pre-merge fixes)

A review of the initial PR found two correctness gaps and a documentation gap, fixed in this same
PR before merge:

- **A bad `--config`/`--baseline` path or an unparseable config could exit 1, "divergent".**
  Criterion 3's "Missing file: exit 3" is not limited to `--legacy`/`--modern`; `CompareCommand.Run`
  only checked those two. A missing `--config`/`--baseline` file, or a `--config` file that is not
  valid JSON at all (`EquivConfigLoader.Load` throws `EquivConfigParseException` for that case, as
  opposed to a wrong-shaped-but-valid JSON document, which it reports as diagnostics instead), would
  reach System.CommandLine's default unhandled-exception path: a stack trace and exit `1` — the
  divergent exit code, silently misreporting a usage error as a real regression to CI. Fixed:
  `Run` now checks `File.Exists` for `--config`/`--baseline` before using them and catches
  `EquivConfigParseException` around the load, both returning exit 3 with a stderr message, with a
  test for each path (`Compare_MissingConfigFileExits3`, `Compare_MissingBaselineFileExits3`,
  `Compare_InvalidConfigJsonExits3`).
- **Config diagnostics were silently dropped.** `EquivConfigLoader.Load(...).Config` already
  replaces every invalid or missing field with its default (by design, per its own doc comment: "a
  caller can proceed after only warning"), but nothing wrote the accompanying `Diagnostics` anywhere,
  so e.g. `"bound": 0` silently fell back to the default bound with no signal to the user. Fixed:
  a new `CompareCommand.LoadConfig` writes each diagnostic (`Id`, `Path`, `Message`) to stderr before
  returning the defaulted config; criterion 8 permits this ("diagnostics go to stderr"). Test:
  `Compare_WarnsOnInvalidConfigValues`.
- The review's third point (undocumented `FrontendLoadException` members) is answered by the
  `Decision` entry at the top of this Notes section, added in this same fix pass.
- **Project-level gap, not this ticket's to fix:** `Matching.MatchResult.Ambiguous` still has no
  ticket wiring it to `Unknown(UnmatchedOverload)` (see the Decision above); the review asked that
  M2-002 or M3-003's goal name it explicitly rather than leaving it to be rediscovered again. Now
  M3-003's goal and criterion 7, with `CompareCommand.cs`/`CompareCommandTests.cs` added to its
  Files list so a future agent working that ticket doesn't read "nothing beyond them" as forbidding
  the very files criterion 7 asks it to touch.

## Second review pass

- **A corrupt `--baseline` file still exited 1.** `TryLoadInputs` checked the baseline path
  *exists* but not that `SarifLog.Load` could actually parse it; a `--baseline` file that exists but
  isn't valid SARIF threw `Newtonsoft.Json.JsonReaderException`/`JsonSerializationException`
  (confirmed both derive from `Newtonsoft.Json.JsonException` via a throwaway probe against
  `Sarif.Sdk` 5.7.0, the same way the config-parse fix in the first review pass was verified)
  straight out of `Run`, landing on System.CommandLine's default handler: exit 1, the same
  divergent-miscoded-as-usage-error bug as the first review's config/baseline point, just for a
  malformed baseline instead of a missing one. Fixed: `TryLoadInputs` now wraps `SarifLog.Load` in
  a `catch (JsonException)`, exit 3 with a message on stderr. Test: `Compare_InvalidBaselineJsonExits3`.
  `Newtonsoft.Json` needed no new `PackageReference`/ADR-0002 row: it is already on `Equiv.Cli`'s
  compile closure transitively through `Equiv.Core`'s `Sarif.Sdk` dependency.
- Test count for `Equiv.Cli.Tests` is now 21, one past the ticket's Size guard ("more than 8 files
  in `src/` or more than 20 tests" means "you have misread the ticket; re-read Out of scope"). All
  of the extra tests past the ticket's original 14 are review-mandated correctness fixes for
  exit-code miscoding (missing/invalid config, missing/invalid baseline, dropped warnings, a fixed
  baseline regression) — not a misreading of scope, so they were kept as separate, clearly-named
  tests rather than folded into existing ones to stay under the number.
