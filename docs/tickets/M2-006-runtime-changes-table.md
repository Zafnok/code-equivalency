# M2-006 Runtime-changes table (EQ006)
Status: done (PR #67)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-003

## Goal
A data file names BCL members whose behaviour differs between .NET Framework 4.8 and
.NET 10 even when the call is textually identical. Calls to them are tagged in the IR
so the backend never treats old and new as the same function, and the SARIF result
links the Microsoft breaking-change page.

## Spec references
VERIFICATION-MODEL.md section 3 (runtime-changed APIs), section 6 (EQ006); ADR 0008.

## Acceptance criteria (all must hold; nothing beyond them)
1. `src/Equiv.Core/RuntimeChanges/runtime-changes.json` (embedded resource) with schema
   `[{ "member": "<identity prefix>", "reason": "<one sentence>", "url": "<https://learn.microsoft.com/...>" }]`.
   `member` is matched as a prefix against `CallIdentity.Value` so one row can cover
   all overloads (`System.String::IndexOf(`).
2. Initial rows, exactly these families: `System.String::IndexOf(`, `LastIndexOf(`,
   `StartsWith(`, `EndsWith(`, `Compare(`, `CompareTo(` (culture-sensitive overloads only:
   the row `reason` says so and the matcher excludes overloads whose last parameter type
   is `System.StringComparison` with an ordinal value, which we cannot see, so the
   whole family is flagged and the config can suppress); `System.String::GetHashCode(`;
   `System.Globalization.CompareInfo::`; `System.Text.Encoding::get_Default(`;
   `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::`;
   `System.Double::ToString(`, `System.Single::ToString(`, `System.Double::Parse(`
   (formatting and parsing changes in .NET Core 3.0). x87 floating point is NOT a row
   (floats are `Sort` in the MVP; noted in the file header comment as a post-MVP row).
3. `RuntimeChangeTable.Load()` reads the resource once; `TryMatch(CallIdentity, out RuntimeChange)`.
   A test asserts every row's URL is `https://learn.microsoft.com/` prefixed and the
   member prefix parses as an identity prefix.
4. The frontend marks matching `IrCall`s: `CallIdentity` gains a Bool
   `RuntimeChanged` flag (record property, default false); `IrText` dumps it as a `!`
   suffix on the callee. `IrCall` itself does not change shape.
5. `equiv.config.json` gains `"suppressRuntimeChanges": ["<member prefix>", ...]`;
   suppressed members are not flagged. Config loader validates entries are non-empty.
6. Backend contract (implemented in M3-001, stubbed here): a flagged call uses
   side-specific uninterpreted functions, and any pair containing a flagged call that
   reaches an observable reports `Divergent` with ruleId EQ006. `VerdictRule` gains
   EQ006 with the runtime-change `reason` and `url` in `message.text` and `helpUri`.
   The SARIF writer test covers EQ006 rendering with a hand-built verdict.

## Files
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, `RuntimeChange.cs`,
`RuntimeChangeTable.cs`; edits to `CallIdentity.cs`, `IrText.cs`, `EquivConfig.cs`,
`EquivConfigLoader.cs`, `VerdictRule.cs`, `SarifReportWriter.cs`; one edit in the
frontend's `CallIdentityFactory`.

## Tests
`Table_LoadsAndValidatesRows`, `Table_MatchesByPrefix`, `Table_SuppressionRemovesMatch`,
`Config_ParsesSuppressRuntimeChanges`, `IrText_RoundTripsRuntimeChangedFlag`,
`Sarif_RendersEQ006WithHelpUri` (snapshot), frontend `Lowering_FlagsIndexOfCall` (snapshot).

## Size guard
No new project, no new package, no more than 3 new files.

## Out of scope
Auto-mapping renamed APIs. The Z3 encoding itself (M3-001 reads the flag).

## Notes
- Decision: `CallIdentity` gains `RuntimeChanged` as a second positional parameter with `= false`, so every existing single-arg call site keeps compiling. Alternatives: a required second positional parameter (breaks every call site), an init-only property outside the primary constructor (inconsistent with "record property" wording and with how the ticket phrases it as part of the identity). Rule: 4.
- Decision: `RuntimeChangeTable.TryMatch` has two overloads: `TryMatch(CallIdentity, out RuntimeChange)` (no suppression) and `TryMatch(CallIdentity, ImmutableArray<string> suppressed, out RuntimeChange)`. `CallIdentityFactory.Of` calls the first; the frontend does not thread `equiv.config.json` through `IrLowerer` (out of the Files list for this ticket), so suppression is applied at the table level and covered by `Table_SuppressionRemovesMatch` directly, ready for M3-001 to call the suppressing overload once it has the config. Alternatives: thread `EquivConfig`/the suppression list through `IrLowerer.Lower` and `CallIdentityFactory.Of` (touches files outside this ticket's Files list). Rule: 4.
- Decision: EQ006 detection lives in `VerdictRule.Describe`, derived from the existing `Divergent.Counterexample`: if any `IrCallRecord` in `Old.Trace` or `New.Trace` has `Callee.RuntimeChanged == true` and matches a table row, the result is EQ006 (reason/url from that row) instead of EQ002. `Divergent`'s shape is unchanged (not in the Files list). Alternatives: add a `RuntimeChange?` field to `Divergent` (changes a Files-list-excluded file and duplicates data already reachable from the trace). Rule: 4.
- Decision: the runtime-change reason and url are rendered as (1) appended text in the SARIF result's `message.text` and (2) a custom result property `helpUri` (`Result.SetProperty("helpUri", url)`), mirroring the existing `"model"` property on `Divergent` results. Sarif.Sdk's `Result` type has no built-in per-result help-link property (only `ReportingDescriptor.HelpUri`, which is one fixed value per rule, not per matched member) — confirmed by reading `Sarif.xml`. Alternatives: a single static `HelpUri` on the EQ006 rule pointing at a generic breaking-changes index (loses the specific per-member link). Rule: 1.
- Decision: `EquivConfig.SuppressRuntimeChanges` (`ImmutableArray<string>`) is an `init`-only property outside the primary constructor, defaulting to `[]`, rather than a fifth positional parameter, so the existing 4-arg call sites in `ConfigEqualityTests` and elsewhere keep compiling. Equality/hash reuse `Equiv.Core.Ir.IrEquality` (already used by `EquivConfigResult` from this namespace). Rule: 4.
- Decision: `runtime-changes.json` is parsed with `JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }`, so its header can carry `//` comments (the x87/Sort note) while the parsed value still matches the ticket's literal `[{ "member", "reason", "url" }]` array schema. Alternatives: a wrapper object with a `"$comment"` field (violates the literal array-rooted schema in criterion 1). Rule: 3.
- Decision: `RuntimeChangeTable.IsIdentityPrefix` (the `Table_LoadsAndValidatesRows` check) only requires the value to split into exactly two parts on `"::"` with a non-empty part before it — matching `ProcedureIdentityNormalizer.Member`'s `Namespace.Type::Member(...)` shape without re-implementing a full identifier grammar. Alternatives: a character-class regex over both halves. Rule: 4.
- Decision: `runtime-changes.json` row URLs point at specific, verified `learn.microsoft.com` pages (fetched and confirmed to load during this ticket) rather than the .NET Core 3.0 aggregate breaking-changes page, so each row's `url` is the actual Microsoft breaking-change/API doc for that behaviour. Rule: 1.
- Decision: `RuntimeChange.Url` is `System.Uri`, not `string`. Analyzer rules CA1054/CA1056 (`AnalysisLevel=latest-all`) fire on a `string`-typed member/parameter named `Url`; `Uri` is also the more precise type. `.OriginalString` is used everywhere the raw text is needed (message text, the SARIF `helpUri` property), so no percent-encoding/normalisation surprises leak in. Rule: 1.
- Decision: `IrTextParser.cs` (not itself in the Files list, but the other half of `IrText`'s dump/parse pair) also gets the `!` symbol added to its tokenizer and `ParseCall`, so `IrText.Parse` inverts the new `!` suffix — required for `IrText_RoundTripsRuntimeChangedFlag` and implied by "`IrText` dumps it as a `!` suffix" needing a matching parse. Rule: 4.
- Note: adding EQ006 to `SarifReportWriter`'s rule list changed every existing SARIF snapshot (the `Driver.Rules` array is always all six rules), including ones in `Equiv.Cli.Tests` and `Equiv.Tests.Integration`; those `.verified.txt`/`.verified.json` files were re-accepted after confirming the only diff was the new `EQ006` rule entry. The pre-existing frontend snapshot `IrLowererSnapshotTests.NullChecks` also changed (it calls `string.CompareTo`, now a flagged member) and was re-accepted the same way.
- Note: `tests/Equiv.Tests.Integration`'s `EndpointDiscoverySampleTests.MatchedPairLowersToTheSameIr` and `MatchesTheActionsByEndpointRouteAlone` fail on this box both before and after this ticket's changes (confirmed by stashing and re-running): the legacy `webapi-basic` sample's `System.Web.Http`/`ApiController` references are unresolved because its non-SDK project was never restored via `MSBuild.exe /t:Restore` (`build.ps1 -Integration`'s "restore samples" step, which needs VS Build Tools + the .NET Framework 4.8 targeting pack per CLAUDE.md). Pre-existing environment gap, not a regression from this ticket.
- Note: the Tests section's suggested names are covered but not spelled identically: `Table_LoadsAndValidatesRows`, `Table_MatchesByPrefix`, `Table_SuppressionRemovesMatch`, `Config_ParsesSuppressRuntimeChanges` and `IrText_RoundTripsRuntimeChangedFlag` are exact method names; the SARIF snapshot is `SarifReportWriterTests.RuntimeChangedDivergent` (+ a focused property/message assertion `RuntimeChangedDivergentResultIsEQ006WithHelpUriAndReasonInTheMessage`) rather than a single `Sarif_RendersEQ006WithHelpUri`; the frontend snapshot is `CallIdentityFactoryTests.Lowering_FlagsIndexOfCall`.
