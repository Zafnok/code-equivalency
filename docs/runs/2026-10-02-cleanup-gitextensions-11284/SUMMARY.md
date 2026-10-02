# cleanup run: gitextensions-11284

- Pair: cleanup, gitextensions/gitextensions PR #11284, legacy ba7b679180c4, modern 89962d9f88b7
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11284, IDE0008 explicit type instead of `var`; no runtime change)
- equiv: 46e6636, mode full, wall-clock 2668s (44m), exit 5 (three pairs crashed in lowering; no
  load failure)
- `--execute`: run, because both sides run on net6.0 and Microsoft.NETCore.App 6.0.36 is installed.
  Wall-clock 3034s, exit 5, the same verdicts as the plain run (see Execution below).

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.000 | +0.000 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.493 | n/a |
| match | 1 | 0.021 | n/a |
| lower | 14532 | 72.561 | +33.022 |
| verify | 14529 | 2554.221 | -411.769 |
| write | 1 | 0.400 | +0.000 |

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes (`run.properties.runtimes`): legacy net6.0 on 45 projects (44 from the
  attribute, 1 from its host) and netstandard2.0, unhosted, on 1; modern the same. No pair crosses
  a runtime, so no runtime rule applies.
- The first attempt exited in one second with no SARIF: the checkout's `global.json` pins SDK
  6.0.401 with `rollForward: feature`, and the box has SDK 10 only. `-Fetch` now patches that
  policy too (this PR). That run is void.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14532 | 14532 |
| analysed lines | 194780 | 194795 |

- Matched pairs 14532; without opaque 9983 (68.7%); whole-body opaque 82 (0.6%); congruent 14317 (98.5%)
- Unchanged share: 98.5% (`pairsCongruent`); the file-level proxy gives 57.9% of lines (1227 of 1695 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 98.5%. Not the row above; ADR 0034.

Replacing `var` with the type it already had leaves most bodies congruent: the pull request edits
468 files and 212 pairs are not congruent.

Top opaque reasons (up to 15):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 2152 | 2175 | P1-014 (ADR 0039) |
| switch-pattern | 1252 | 1252 | P1-014, P1-015, P1-016 (ADR 0039) |
| Binary | 666 | 666 | P2-087 |
| DefaultValue | 506 | 506 | none |
| InstanceReference | 445 | 445 | none |
| DelegateCreation | 245 | 245 | none |
| CaughtException | 132 | 132 | none |
| ImplicitIndexerReference | 120 | 120 | none |
| InterpolatedString | 96 | 96 | P1-014 (ADR 0039) |
| DeconstructionAssignment | 78 | 78 | P1-014 (ADR 0039) |
| iterator | 77 | 77 | none |
| ArrayElementReference | 67 | 67 | none |
| ref-argument | 65 | 65 | none |
| CompoundAssignment | 61 | 61 | P1-014 (ADR 0039) |
| Tuple | 50 | 50 | none |

## Changed code
- Changed pairs 212 of 14532; without opaque 39; whole-body opaque 12
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 18.4%
- 188 of the 212 are in a file the pull request edited. The other 24 are in files that are
  byte-identical on both sides (14 EQ002, 10 EQ003).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| Conversion | 44 | P1-014 |
| "" | 39 | n/a |
| DeconstructionAssignment | 19 | P1-014 |
| switch-pattern | 8 | P1-014 |
| Conversion+DeconstructionAssignment | 7 | P1-014 |
| iterator | 7 | none |
| Conversion+switch-pattern | 5 | P1-014 |
| no-body | 5 | M4-008 (backlog) |
| Conversion+ImplicitIndexerReference | 4 | P1-014 |
| DelegateCreation | 4 | none |
| CaughtException | 3 | none |
| CaughtException+Conversion | 3 | P1-014 |
| ArrayCreation | 2 | none |
| Binary+Conversion | 2 | P1-014, P2-087 |
| Binary+Conversion+DefaultValue+switch-pattern | 2 | P1-014, P2-087 |

`Conversion` is in the reason set of 112 of the 212 changed pairs.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only

## Verdicts
- By rule: EQ001 14367, EQ002 22, EQ003 162, EQ004 0, EQ005 0, EQ006 0. Three pairs have no result:
  they crashed in lowering and are listed in `run.properties.unverified`.
- By proofMethod: congruence 14317, lockstep-induction 45, bounded 5
- Unknown by scope: line 58, method 104 (82 over matched pairs plus 22 `unmatched-overload`).
  Line-scoped Unknown share: 35.8%
- Top Unknown reasons: opaque 110 (line 58, method 52), unmatched-overload 22, timeout 17,
  abstraction 9, unaligned-loop 4
- `unbound` Unknowns: 0
- Top abstractions: `opaque Conversion` 12, `delegate` 8, `opaque DefaultValue` 2
- Review list: 28 groups for 184 flagged results; flagged results as a share of matched pairs: 1.3%.
  Top five: `EQ003 opaque:DeconstructionAssignment: 36`, `EQ003 opaque:Conversion: 31`,
  `EQ003 unmatched-overload: 22`, `EQ003 timeout: 17`, `EQ002 proofMethod:none: 13`.

The 212 changed pairs, by verdict: proved Equivalent 50 (23.6%: lockstep-induction 45, bounded 5),
Divergent 22 (10.4%), Unknown 140 (66.0%). The 22 `unmatched-overload` Unknowns are not matched
pairs and are left out.

## Divergent, adjudicated (P2-047's method)
Each was hand-traced: the two files were compared, and the first call at which the model's two
traces part was read against both bodies. The `--execute` run gave
no replay to go on: all 22 are `not-constructible`.

| Cause | EQ002 | Classification |
|---|---|---|
| A caller-file-path argument differs because the two sides are two checkout directories (P2-098) | 22 | false positive |

Confirmed behaviour changes 0, false positive 22, undetermined 0.

All 22 are tests that call a snapshot-verification method whose last parameter defaults to the
caller's source file path. In every one the first differing call is that method and the only
differing argument is that string. 14 of the 22 are in files that are byte-identical on both
sides. They are the same tests as on `gitextensions-11372`, less the two that rest on a collection
expression there and less `BugReporter.Program::Main()`:
- `GitCommandsTests.GitRevisionSummaryBuilderTests::Should_do_ellipsis(string)`
- `GitCommandsTests.Git.GitRevisionTesterTests::ReverseSelection_expected_changes_none()`, and
  its siblings `_renamed()`, `_new()`, `_deleted()`, `_unmerged()`, `_getdefaults()`
- `GitCommandsTests.Git.Commands.GetAllChangedFilesOutputParserTest::TestGetDefaultStatus()`
- `GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Deserialize_remote_repositories_with_ns()`
  and `::Deserialize_remote_repositories_without_ns()`
- `GitUITests.Hotkey.HotkeySettingsManagerTests::Can_save_settings()`
- `GitUITests.UserControls.RevisionGrid.RevisionGraphTests::SegmentsAreStraightened()`,
  `::SegmentsWithCommitsAreStraightened()`,
  `::SegmentsWithOutgoingSecondaryMergesAreNotStraightened()`,
  `::SegmentsWithIncomingMergesAreStraightened()`,
  `::SegmentsAreStraightenedAlthoughThisCausesWidthIncrease()`,
  `::SegmentsWithOutgoingPrimaryMergesAreStraightened()`,
  `::SegmentsAreNotStraightenedIfThisCausesAShiftForPrimarySegment()`
- `AppVeyorIntegrationTests.AppVeyorAdapterTests::Should_return_a_build_Info_When_Json_content_is_the_one_of_a_pull_request_build()`
  and `::Should_return_a_build_Info_When_Json_content_is_the_one_of_a_master_build()`
- `BugReporterTests.SerializableExceptionTests::ToString(string,System.Action)` and
  `::ToString_should_be_same_from_round_trip(string,System.Action)`

**The cleanup changed no behaviour that this run found.**

## Execution
- Replay: 22 of 22 EQ002 `not-constructible`. Reasons: the model constrains a cast or a field the
  driver cannot set (16), the result is a `Task` the driver cannot compare (3), the divergence is in
  the call trace, which replay does not observe (3). None `reproduced`, none `not-reproduced`.
- Differential testing on the 140 Unknown matched pairs: 49 ran, 91 `notConstructible`. None found
  a difference: no Unknown became Divergent, and no result has `proofMethod: observed`.
- The same three lowering crashes; exit 5.

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- 22 false EQ002 from the caller-file-path constant: P2-098.
- Three lowering crashes, each a null reference, on
  `GitCommands.CommitDataManager::TryGetCommitLog(string,string,out string,out string,bool)`,
  `GitCommands.AppSettings::GetGitExtensionsFullPath()` and
  `GitExtUtils.GitArgumentBuilder::ToString()`. They make the run exit 5: P2-095.
- 17 timeouts: P2-083 (timeouts), open.
- The solver proved 50 of 212 changed pairs (23.6%). 66.0% are still Unknown, 110 of the 140 for
  an opaque node.
