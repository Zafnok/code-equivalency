# full+seeded+execute run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream PR #8522, .NET Framework 4.8 to .NET 5)
- equiv: fd400e9, wall-clock full 9450s (2h37m) exit 5; seeded 9964s (2h46m) exit 5; full --execute 10096s (2h48m) exit 5. Exit 5 is the pair-level crash code (92 pairs), not a failed run. Mechanical seeding: seeder crashed (P2-035).

## Load
- Projects: legacy and modern both loaded every C# project in the default build configuration; skipped: none
- Project load rate: 100% (0 skipped of the C# projects the solution builds)
- Not built (outside the default configuration, ADR 0028 clarification 2026-09-24): legacy 1 (`Setup`), modern 1 (`Setup`)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13708 | 13556 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13541; without opaque 8914 (65.8%); whole-body opaque 158 (1.2%); congruent 12341 (91.1%)
- Unchanged share: 91.1% (pairsCongruent / matchedPairs)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 91.2%. Not the row above; ADR 0034.

Top opaque reasons (legacy/modern) and owner:

| reason | legacy | modern | owning ticket |
|---|---|---|---|
| DelegateCreation | 1370 | 1369 | M4-004 |
| switch-pattern | 1197 | 1194 | IOPERATION-COVERAGE rows for patterns |
| Conversion | 760 | 759 | M2-004, M3-010, M4-002, M4-005 |
| Binary | 651 | 651 | M2-004, M4-002 |
| InstanceReference | 374 | 374 | M2-004 |
| EventAssignment | 367 | 367 | P2-005 |
| InterpolatedString | 331 | 329 | M4-004 |
| DefaultValue | 314 | 314 | P2-003 |
| CaughtException | 123 | 122 | M2-004, M4-008 |
| FlowCaptureReference | 110 | 110 | M2-003 |
| DeconstructionAssignment | 80 | 80 | P2-025 (new) |
| iterator | 73 | 73 | M3-026, M4-006 |
| ArrayElementReference | 62 | 62 | M2-004, P1-006 |
| CompoundAssignment | 58 | 58 | M2-004, M3-010, P2-022, P2-004 |
| Tuple | 54 | 54 | P2-027 (new) |

Reasons with no prior owner, each filed: AddressOf (P2-023), AnonymousObjectCreation (P2-024), DeconstructionAssignment (P2-025), TranslatedQuery (P2-026), Tuple (P2-027), TypeParameterObjectCreation (P2-028), DynamicInvocation (P2-029), SizeOf (P2-030).

## Changed code
- Changed pairs 1197 of 13541; without opaque 447; whole-body opaque 14
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 37.3%

Top reason sets (changed pairs): "" (no opaque) 447; DelegateCreation 202; switch-pattern 80; DelegateCreation+switch-pattern 42; Binary 39; InterpolatedString 26; Conversion 22; DelegateCreation+InterpolatedString 16; iterator 14; CaughtException 13; InterpolatedString+switch-pattern 10; Binary+Conversion 9; CompoundAssignment 9; EventAssignment 9; Binary+switch-pattern 8.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1223 | 1224 |
| distinct members | 208 | 206 |
| pairs with any | 684 | 686 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only. Version-changed (first 15): adystech.credentialmanager 1.7.0 -> 2.1.1; appinsights.windowsdesktop 2.10.42-preview -> 2.13.1; ben.demystifier 0.1.4 -> 0.1.6; castle.core 4.2.0 -> 4.4.0; excss 2.0.6 -> 4.1.0; fluentassertions 5.2.0 -> 5.10.3; jetbrains.annotations 2018.2.1 -> 2020.1.0; libgit2sharp 0.25.0 -> 0.26.2; libgit2sharp.nativebinaries 1.0.210 -> 2.0.306; microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1; microsoft.codecoverage 15.9.0 -> 16.6.1; microsoft.net.test.sdk 15.9.0 -> 16.6.1; microsoft.visualstudio.composition 15.6.36 -> 16.4.11; microsoft.visualstudio.threading 16.5.132 -> 16.7.56; newtonsoft.json 10.0.3 -> 12.0.3

## Verdicts (full, no --execute)
- By rule: EQ001 12414, EQ002 82, EQ003 700, EQ004 15, EQ005 167, EQ006 272
- By proofMethod: congruence 12341, bounded 61, lockstep-induction 12, n/a 1236
- Unknown by scope: line 142, method 558. Line-scoped Unknown share: 20.3% (142 of 700)
- Top Unknown reasons: abstraction 261, opaque 197, timeout 161, unaligned-loop 58, unmatched-overload 19, recursion 4
- Pair-level crashes: 92 (92 tool-execution notifications, 92 unverified procedures). By cause: `domain sort ... do not match` between a derived and a base type, or other type pairs, 47 (P2-031); nullable array or `T`/`T?` sort mismatch 34 (P2-032); `IrSortValue` key not found 8 (P2-033); lowering NullReferenceException 2 (P2-034); query `from` clause cast 1 (P2-026)
- Nondeterminism: the `--execute` run's pre-execution verdicts differ from this run by 2 results (EQ006 272 -> 270), so a run with solver timeouts is not bit-for-bit repeatable

## Tests (full only)
- The human pair has no `verify_command` in the manifest, and the upstream suite is WinForms UI tests that need a desktop session and a Git install; not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a (criterion 4 cannot be evaluated for this pair; the agent pairs' tests all pass on the modern side, see their summaries)

## Seeds (seeded, hand-written)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S01 | `System.StringExtensions::RemovePrefix(string,string,StringComparison)` | EQ006 Divergent (runtime-change) | n/a (whole overload family flagged, does not isolate the seed) |
| S02 | `GitCommands.PathUtil::GetFileName(string)` | EQ002 Divergent | n/a |
| S03 | `GitCommands.Git.GitRevisionTester::Matches(GitRevision,string)` | EQ003 Unknown(opaque) | yes |
| S04 | `GitCommands.LockableNotifier::CheckNotify(int)` | EQ002 Divergent | n/a |
| S05 | `GitCommands.Logging.CommandLogEntry::get_Detail()` | EQ003 Unknown(timeout) | no (blast-radius miss) |
| S06 | `GitCommands.GitRevisionInfoProvider::LoadChildren(IGitItem)` | EQ003 Unknown(abstraction) | no (blast-radius miss) |
| S07 | `GitCommands.EncodingHelper::GetString(byte[],byte[],Encoding)` | EQ003 Unknown(opaque) | yes |
| S08 | `GitUI.Globals::IsInRange(int,int,int)` | EQ002 Divergent | n/a |

- Seeded recall: 8/8 = 100% (ADR 0028 / `tools/corpus/seeds.md` rule 5). Line-pointing: 6/8; 2 blast-radius misses (S05, S06).
- Seeded run totals: EQ001 12405, EQ002 87, EQ003 708, EQ004 15, EQ005 167, EQ006 269.

## Mechanical seeds (seeded, ticket M4-010)
- Seeds: requested 300, applied 0. The seeder crashed (`InvalidOperationException: The item specified is not the element of a list`) before applying any mutation, at `--seed 1`, the same way it did on the Tomas pair. P2-035.
- Recall: n/a

## Replay (full --execute, M4-009)
- Divergent results replayed: 352 (EQ002 plus EQ006). By value: `not-constructible` 331, `reproduced` 5, `not-reproduced` 16
- `not-constructible` reasons (331): the divergence is in the call trace, which replay does not observe 125; legacy method not public 105; the model constrains a heap map, cast, typeof or field 57; no public parameterless constructor 28; not a method or property getter 7; no argument can be built for a parameter 8; by-reference parameter 1
- `reproduced` (real runtimes disagree, as the model said): `GitCommands.AppSettings::GetDictionaryDir()`, `GitCommands.AppSettings::GetGitExtensionsDirectory()`, `GitCommandsTests.Config.ConfigFileTest::TestWithHiddenFile()`, `EasyHook.LocalHook::Release()`, `GitExtensions.UITests.NBugReports.BugReportFormTests::Should_show_load_exception_info_correctly()`
- `not-reproduced` (each is a ticket): 16 results. One solver Divergent, `GitCommands.GitSshHelpers::SetSsh(string)` (P2-037). Fifteen EQ006 results (P2-038): `ScriptOptionsParser::DependsOnSelectedRevision`, `EnvironmentAbstraction::SetEnvironmentVariable`, `GitModule::TryFindGitWorkingDir`, `GitSshHelpers::UnsetSsh`, `PathUtil::IsLocalFile`, `PathUtil::Combine`, `PathUtil::GetDisplayPath`, `StringExtensions::SubstringUntil`, `SubstringUntilLast`, `SubstringAfter`, `SubstringAfterLast`, `BuildServerSettingsHelper::IsRegexValid`, `GitProtocolExtensions::IsUrlUsingHttp`, `Git.hub.Repository::GetHashCode`, `DocumentFactory::CreateFromFile`
- Differential testing of Unknown pairs: 126 completed (stopped by target 125, by budget 1; one species for 104 of them), 503 `notConstructible`
- **Unknown became Divergent by observation (`proofMethod: observed`): 54.** They are behaviour differences seen by running both builds, so each is a real .NET Framework 4.8 versus .NET 10 difference on some input. Production-code examples: `GitExtUtils.GitUI.DpiUtil::Scale(int)`, `Scale(int,int)`, `GitCommands.AppSettings::GetResourceDir()`, `GitCommands.Git.GitDirectoryResolver::Resolve(string)`, `GitCommands.MoveNamespaceDeserializationBinder::BindToType(string,string)`, `GitUI.UserControls.RevisionGrid.Columns.GraphCache::Allocate(int,int,int)`, `GitUI.UserManual.SingleHtmlUserManual::get_Location()`, `GitUI.Infrastructure.Telemetry.DiagnosticsClient::Initialize(bool)`, `ICSharpCode.TextEditor.Document.FontContainer::get_TwipsPerPixelY()`. The remaining 45 are test methods.
- Run totals: EQ001 12414, EQ002 136, EQ003 648, EQ004 15, EQ005 167, EQ006 270.

## Believability of Divergent results (M4-007 criterion 5)
The user delegated this call to the implementing agent ("do whatever you want"), so these are the
agent's picks, not the user's. The evidence behind each is the `--execute` replay and differential
testing above, so the choice is not a matter of taste.

Most believable:
1. `GitCommands.AppSettings::GetGitExtensionsDirectory()`: the solver's counterexample, replayed on
   .NET Framework 4.8 and .NET 10, gave different outcomes (`replay: reproduced`). Two independent
   oracles agree.
2. `GitCommands.AppSettings::GetDictionaryDir()`: same reason, and it builds a path from the same
   application-directory lookup, which is exactly where the two runtimes differ (EQ006's
   runtime-change table).
3. `GitCommands.Git.GitDirectoryResolver::Resolve(string)`: not proved by the solver at all; running
   both builds on generated inputs showed different behaviour (`proofMethod: observed`). An observed
   difference in production code cannot be a modelling artefact.

Least believable:
1. `GitCommands.GitSshHelpers::SetSsh(string)`: the model's legacy side throws, the real legacy
   runtime returned (`replay: not-reproduced`). The model and the CLR disagree (P2-037).
2. `Git.hub.Repository::GetHashCode()`: flagged for `String.GetHashCode` randomisation, which is
   real, but replay threw on both sides, so it never observed a value (P2-038).
3. `GitCommands.PathUtil::Combine(string,string)`: flagged as a runtime-changed API, replay under
   the invariant culture saw identical results, and nothing in the method depends on culture (P2-038).

## Findings
- P2-031 domain-sort mismatch on inherited `this` (the largest crash family, 47 of 92)
- P2-032 nullable array sort mismatch
- P2-033 `IrSortValue` key not found (also crashes Tomas)
- P2-034 lowering NullReferenceException (2 methods)
- P2-026 TranslatedQuery, including the `from` clause cast crash in `RecentRepoSplitter::SplitRecentRepos`
- P2-023..P2-030 eight opaque reasons with no owner
- P2-037, P2-038 replay `not-reproduced`
- P2-035 seeder crash (Git Extensions and Tomas)
- P2-039 `--execute` deadlock (Tomas)
