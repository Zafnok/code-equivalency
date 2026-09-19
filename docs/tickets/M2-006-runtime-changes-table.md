# M2-006 Runtime-changes table (EQ006)
Status: todo
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
