# cleanup run: gitextensions-11372

- Pair: cleanup, gitextensions/gitextensions PR #11372, legacy 030255ef17cb, modern 1cfb0e4441e2
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11372, IDE0028 collection expressions; no runtime change)
- equiv: 46e6636, mode full, wall-clock 16979s (4h43m), exit 5 (one pair crashed in lowering; no
  load failure). The box was shared with another session's corpus run for the whole time.
- `--execute`: not run. Both sides run on net8.0, and this box has Microsoft.NETCore.App 6.0.36,
  10.0.9 and 10.0.12 only. ADR 0040 decision 3 never substitutes another runtime.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.000 | +0.000 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.704 | n/a |
| match | 1 | 0.058 | n/a |
| lower | 14560 | 90.566 | +42.636 |
| verify | 14559 | 16829.711 | +3921.441 |
| write | 1 | 0.341 | +0.000 |

Two pairs took 8,330 of the 16,830 verify seconds, far past any solver budget (P2-076):
`GitUI.CommandsDialogs.FormCommit::.ctor(GitUI.GitUICommands,GitUI.CommandsDialogs.CommitKind,GitUIPluginInterfaces.GitRevision,string)`
at 5,432s and `GitUI.LeftPanel.Nodes::FillTreeViewNode(System.Windows.Forms.TreeNode)` at 2,898s.
Both ended Unknown.

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes (`run.properties.runtimes`): legacy net8.0 on 45 projects (44 from the
  attribute, 1 from its host) and netstandard2.0, unhosted, on 1; modern the same. No pair crosses
  a runtime, so no runtime rule applies.
- The first attempt exited in one second with no SARIF: the checkout's `global.json` pins SDK
  8.0.0 with `rollForward: feature`, and the box has SDK 10 only. `-Fetch` now patches that
  policy too (this PR). That run is void.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14560 | 14560 |
| analysed lines | 195107 | 195129 |

- Matched pairs 14560; without opaque 10005 (68.7%); whole-body opaque 83 (0.6%); congruent 14208 (97.6%)
- Unchanged share: 97.6% (`pairsCongruent`); the file-level proxy gives 70.6% of lines (1511 of 1707 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.6%. Not the row above; ADR 0034.

Top opaque reasons (up to 15):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 2151 | 2174 | P2-099 for the collection-expression part; P1-014 (ADR 0039) |
| switch-pattern | 1246 | 1246 | P1-014, P1-015, P1-016 (ADR 0039) |
| Binary | 681 | 681 | P2-087 |
| DefaultValue | 499 | 499 | none |
| InstanceReference | 448 | 448 | none |
| DelegateCreation | 248 | 248 | none |
| CaughtException | 132 | 132 | none |
| ImplicitIndexerReference | 121 | 121 | none |
| InterpolatedString | 99 | 99 | P1-014 (ADR 0039) |
| DeconstructionAssignment | 79 | 79 | P1-014 (ADR 0039) |
| iterator | 77 | 77 | none |
| ArrayElementReference | 67 | 67 | none |
| CompoundAssignment | 61 | 61 | P1-014 (ADR 0039) |
| ref-argument | 61 | 61 | none |
| Tuple | 51 | 50 | none |

## Changed code
- Changed pairs 351 of 14560; without opaque 23; whole-body opaque 8
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 6.6%
- 322 of the 351 are in a file the pull request edited. The other 29 are in files that are
  byte-identical on both sides (13 EQ002, 16 EQ003); see the adjudication below for why they are
  not congruent.

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| Conversion | 192 | P2-099 |
| Conversion+switch-pattern | 29 | P2-099 |
| "" | 23 | n/a |
| Binary+Conversion | 9 | P2-099, P2-087 |
| no-body | 6 | M4-008 (backlog) |
| switch-pattern | 5 | P1-014 |
| Binary+Conversion+switch-pattern | 4 | P2-099, P2-087 |
| Conversion+DefaultValue | 4 | P2-099 |
| Conversion+DefaultValue+switch-pattern | 4 | P2-099 |
| Conversion+DelegateCreation | 4 | P2-099 |
| Conversion+ImplicitIndexerReference | 4 | P2-099 |
| ArrayCreation+Conversion | 3 | P2-099 |
| Binary+Conversion+DefaultValue | 3 | P2-099, P2-087 |
| CaughtException+Conversion | 3 | P2-099 |
| CaughtException | 2 | none |

`Conversion` is in the reason set of most changed pairs because a collection expression lowers as
an opaque conversion to its target type. That one construct is this pull request.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only

## Verdicts
- By rule: EQ001 14215, EQ002 25, EQ003 342, EQ004 0, EQ005 0, EQ006 0. One pair has no result: it
  crashed in lowering and is listed in `run.properties.unverified`.
- By proofMethod: congruence 14208, bounded 7
- Unknown by scope: line 171, method 171 (148 over matched pairs plus 23 `unmatched-overload`).
  Line-scoped Unknown share: 50.0%
- Top Unknown reasons: opaque 261 (line 171, method 90), timeout 43, unmatched-overload 23,
  unaligned-loop 7, abstraction 6, recursion 2
- `unbound` Unknowns: 0
- Top abstractions: `opaque Conversion` 10, `delegate` 4
- Review list: 27 groups for 367 flagged results; flagged results as a share of matched pairs: 2.5%.
  Top five: `EQ003 opaque:Conversion: 227`, `EQ003 timeout: 43`, `EQ003 unmatched-overload: 23`,
  `EQ002 proofMethod:none: 14`, `EQ003 unaligned-loop: 7`.

The 351 changed pairs, by verdict: proved Equivalent 7 (2.0%, all `bounded`), Divergent 25 (7.1%),
Unknown 319 (90.9%). The 23 `unmatched-overload` Unknowns are not matched pairs and are left out.

Proved Equivalent by the solver:
- `GitUI.FileStatusList::GetNextIndex(bool,bool)`
- `GitUI.CommandsDialogs.FormCheckoutBranch::PopulateBranches()`
- `GitUI.CommitInfo.CommitInfo::ReloadCommitInfo()`
- `GitUI.ScriptsEngine.ScriptsManager::GetScripts()`
- `GitUI.UserControls.BranchSelector::Initialize(bool,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.ObjectId>)`
- `GitCommands.GitModule::GetRemotesAsync()`
- `GitCommandsTests.ArgumentBuilderExtensionsTests::Handles_all_enum_members()`

## Divergent, adjudicated (P2-047's method)
No replay exists (`--execute` unavailable), so each was hand-traced: the two files were compared,
and the first call at which the model's two traces part was read against both bodies.

| Cause | EQ002 | Classification |
|---|---|---|
| A caller-file-path argument differs because the two sides are two checkout directories (P2-098) | 22 | false positive |
| A build-generated commit constant differs between any two commits (P2-098) | 1 | real, by construction; not a behaviour change of the pull request |
| A collection expression and the initializer it replaces give different call traces (P2-099) | 2 | false positive |

Confirmed behaviour changes 0, false positive 24, real by construction 1, undetermined 0.

- **Caller file path, 22.** Each is a test that calls a snapshot-verification method whose last
  parameter defaults to the caller's source file path. The compiler fills in the absolute path,
  and the two sides were fetched into two directories, so the constant differs. In every one of the
  22 the first differing call is that method and the only differing argument is that string. 12
  of the 22 are in files that are byte-identical on both sides. Built from one directory, the two
  bodies pass the same string.
  - `GitCommandsTests.GitRevisionSummaryBuilderTests::Should_do_ellipsis(string)`
  - `GitCommandsTests.Git.GitRevisionTesterTests::ReverseSelection_expected_changes_none()`, and
    its siblings `_renamed()`, `_new()`, `_deleted()`, `_unmerged()`, `_getdefaults()`
  - `GitCommandsTests.Git.Commands.GetAllChangedFilesOutputParserTest::TestGetDefaultStatus()`
  - `GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Deserialize_remote_repositories_with_ns()`
    and `::Deserialize_remote_repositories_without_ns()`
  - `GitUITests.Hotkey.HotkeySettingsManagerTests::Can_save_settings()`
  - `GitUITests.UserControls.RevisionGrid.RevisionGraphTests::SegmentsAreStraightened(bool)`,
    `::SegmentsWithCommitsAreStraightened(bool)`,
    `::SegmentsWithOutgoingSecondaryMergesAreNotStraightened(bool)`,
    `::SegmentsWithIncomingMergesAreStraightened(bool)`,
    `::SegmentsAreStraightenedAlthoughThisCausesWidthIncrease(bool)`,
    `::SegmentsWithOutgoingPrimaryMergesAreStraightened(bool)`,
    `::SegmentsAreNotStraightenedIfThisCausesAShiftForPrimarySegment(bool)`
  - `AppVeyorIntegrationTests.AppVeyorAdapterTests::Should_return_a_build_Info_When_Json_content_is_the_one_of_a_pull_request_build()`
    and `::Should_return_a_build_Info_When_Json_content_is_the_one_of_a_master_build()`
  - `BugReporterTests.SerializableExceptionTests::ToString(string,System.Action)` and
    `::ToString_should_be_same_from_round_trip(string,System.Action)`
- **Commit constant, 1.** `BugReporter.Program::Main()` is in a file the pull request did not
  touch. It passes a constant that the build generates from the commit hash, and the two sides are
  two commits. The reported difference is real and is not something the cleanup did.
- **Collection expression, 2.**
  `GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Serialize_recent_repositories()`
  and
  `GitCommandsTests.UserRepositoryHistory.Legacy.RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`.
  The edit replaces a target-typed `new()` with a collection initializer by a collection
  expression holding the same elements in the same order. The legacy trace starts with the list's
  constructor and the modern trace with the first element's constructor, and the model has that
  first call throw on each side. The list constructor does not throw, and both forms build the same
  list. These two also call the snapshot method, so they would stay flagged for the first cause.

**The cleanup changed no behaviour that this run found.**

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- 23 false or by-construction EQ002 from constants that say where or from which commit the code
  was built: P2-098. They also explain why pairs in byte-identical files are not congruent.
- A collection expression is an opaque `Conversion`. It is in the reason set of 308 of the 351
  changed pairs and behind 2 false EQ002: P2-099.
- One lowering crash, a null reference, on
  `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`. It makes the run
  exit 5: P2-105.
- 43 timeouts, and two pairs that ran 91 and 48 minutes: P2-076 and P2-101, both open.
- The solver proved 7 of 351 changed pairs (2.0%). On Git Extensions' migration (P2-046) it
  proved 77 of 1143 (6.7%).
