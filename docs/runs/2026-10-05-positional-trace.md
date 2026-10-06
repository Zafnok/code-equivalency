# P1-038 positional trace in rung 1: gitextensions-8522 (2026-10-05)

Question: when rung 1 compares the call traces by position instead of as two sequences, which results of a
`full` run change, and does any result that was decided become a timeout?

**Answer: 106 of the 210 pairs whose rung 1 query Z3 gave up on are now answered, and none that was
answered is now given up on. `timeout` Unknowns fall from 148 to 80, and the `verify` phase takes 1,464 s
against 3,169 s. Of the 106, 48 are a divergence that replays to a Divergent, 54 a divergence that depends
on an abstraction, and four have a `divergence` query that is now proved unsatisfiable and reach an opaque
node. No pair is newly proved. Twelve pairs that rung 1 answered both times
moved between Divergent and Unknown(`abstraction`), six each way, because Z3 found another model of the
same satisfiable query. The size guard does not apply and the cap stays at 10,000 pairs of sites.**

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f.
- Before: `main` at `4c80d9b0`, this branch's base. After: this branch at `d8173087`. The two differ by
  P1-038 alone. The product has no switch that picks the comparison, so the two runs are two builds.
- Each run is `equiv compare` in `full` mode at the default config (`resourceLimit` 5,000,000, `timeoutMs`
  60,000) with `--jobs 4 --verbosity debug`, Release, no second solver. Both exit 1.
- Machine: one Windows box, 24 cores, 63 GB. The two runs ran side by side from the end of the first one's
  load phase; the before run ran alone once the after run had ended. Wall-clock: before 4,487 s, after
  2,745 s.

## Phase times

| phase | items | before, seconds | after, seconds |
|---|---|---|---|
| lower | 13,541 | 150 | 138 |
| verify | 13,541 | 3,169 | 1,464 |
| contracts | 841 | 1,107 | 1,096 |

Rungs ended by a limit (`run.properties.queryEndings`): the resource limit 254 before and 134 after, the
wall-clock backstop 10 before and 3 after.

## Results by verdict and Unknown reason

13,742 results in both runs, the same identities.

| result | before | after |
|---|---|---|
| EQ001 `congruence` | 12,682 | 12,682 |
| EQ001 `bounded` | 40 | 40 |
| EQ001 `lockstep-induction` | 5 | 5 |
| EQ002 | 20 | 24 |
| EQ006 | 205 | 240 |
| EQ003 `timeout` | 148 | 80 |
| EQ003 `abstraction` | 139 | 193 |
| EQ003 `opaque` | 265 | 264 |
| EQ003 `unaligned-loop` | 36 | 12 |
| EQ003 `recursion` | 1 | 1 |
| EQ003 `unmatched-overload` | 19 | 19 |
| EQ004 | 15 | 15 |
| EQ005 | 167 | 167 |

Divergent (EQ002 and EQ006) goes from 225 to 264 and Unknown from 608 to 569. Proved Equivalent is 12,727
in both.

## Rung 1's outcome, pair by pair

859 results have a ladder. Rung 1's step on each, before against after:

| before | after | pairs |
|---|---|---|
| timeout | timeout | 104 |
| timeout | refuted (a divergence that replays) | 48 |
| timeout | inconclusive: a divergence that depends on an abstraction | 54 |
| timeout | inconclusive: no divergence, and an input reaches an opaque node | 4 |
| refuted | refuted | 207 |
| refuted | inconclusive | 6 |
| inconclusive | refuted | 6 |
| inconclusive | inconclusive | 389 |
| proved | proved | 40 |
| not applicable | not applicable | 1 |

No row goes from an answer to a timeout. P1-034 measured 72 of 142 on the queries of an earlier commit, four
of them unsatisfiable; here 106 of 210 are answered, four of them unsatisfiable.

## The `timeout` Unknowns that moved

68 of the 148 are no longer `timeout`:

| after | pairs |
|---|---|
| EQ006 | 24 |
| EQ002 | 2 |
| EQ003 `abstraction` | 39 |
| EQ003 `opaque` | 3 |

The other 38 pairs whose rung 1 query is now answered had another reason before, because a later rung
gave the result once rung 1 had given up:

| before | after | pairs |
|---|---|---|
| EQ003 `unaligned-loop` | EQ006 | 13 |
| EQ003 `unaligned-loop` | EQ003 `abstraction` | 11 |
| EQ003 `opaque` (an induction obligation reached an opaque node) | EQ006 | 3 |
| EQ003 `opaque` | EQ003 `abstraction` | 1 |
| EQ006 (rung 2 refuted the pair) | EQ003 `abstraction` | 3 |
| the same result (EQ006 3, EQ002 3, EQ003 `opaque` 1) | | 7 |

## Results that were not `timeout` before and are after

None. No result is a `timeout` Unknown after that was anything else before, and no pair's rung 1 step is a
timeout after that was an answer before. The size guard's fallback (the sequence form first, the positional
one when Z3 gives up) is not built.

## Results that moved without a timeout

Twelve pairs whose rung 1 query Z3 answered satisfiable in both runs have another result. A satisfiable
query has many models, the two forms lead Z3 to different ones, and the replay of one model is a divergence
where the replay of another depends on an abstraction (ADR 0026). Neither result is wrong for its model.

| pair | before | after |
|---|---|---|
| `GitCommands.GitModule::GetSelectedBranchFast(string,bool)` | EQ003 `abstraction` | EQ006 |
| `GitExtensions.UITests.CommandsDialogs.FormEditorTests::HasChanges_updated_correctly()` | EQ003 `abstraction` | EQ006 |
| `GitUI.Avatars.TemplateFormatter::Create<TInput>(string,System.Func<string, System.Func<TInput, string>>)` | EQ003 `abstraction` | EQ006 |
| `GitUITests.CommandsDialogs.FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(string,bool,bool)` | EQ003 `abstraction` | EQ006 |
| `GitUI.CommandsDialogs.RevisionFileTreeController::SelectFileOrFolder(GitUI.UserControls.NativeTreeView,string)` | EQ003 `abstraction` | EQ002 |
| `GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::IsCurrentlyOpenedWorktree(GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree.WorkTree)` | EQ003 `abstraction` | EQ002 |
| `GitUI.Avatars.CustomAvatarProvider::FromTemplateSegment(GitUI.Avatars.IAvatarDownloader,string)` | EQ006 | EQ003 `abstraction` |
| `GitUI.CommandsDialogs.FormGitIgnore::AddDefaultClick(object,System.EventArgs)` | EQ006 | EQ003 `abstraction` |
| `GitUI.CommandsDialogs.FormRemotesController::RemoteUpdate(System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository>,string,string)` | EQ006 | EQ003 `abstraction` |
| `GitUI.Editor.FileViewerInternal::GetTextMarkersMatchingWord(string)` | EQ006 | EQ003 `abstraction` |
| `GitUI.Theming.ThemePathProvider::GetThemePath(GitExtUtils.GitUI.Theming.ThemeId)` | EQ006 | EQ003 `abstraction` |
| `GitUIPluginInterfaces.Tests.PluginsPathScannerTests::PathScanning(string,string[])` | EQ006 | EQ003 `abstraction` |

Six Divergent results became Unknown this way and six Unknown became Divergent. The three pairs that rung 2
refuted before and rung 1 now answers with a model that depends on an abstraction are in the table of the
section above: `GitCommands.Git.GetAllChangedFilesOutputParser::GetAllChangedFilesFromString_v2(string)`,
`GitCommands.GitRefName::GetRemoteName(string,System.Collections.Generic.IEnumerable<string>)` and
`GitCommands.Patches.PatchManager::GetSelectedChunks(string,int,int,out string)`. They are Unknown where
they were Divergent because rung 1 ends the ladder when it finds a divergence of either kind.

## The cap
`PositionalTrace.MaxPairs` is 10,000 pairs of call sites that can meet, P1-034's observation that Z3
decided no query above it. With it no answered query is lost, so it stays. The run does not count how many
products were above it and kept the sequence form; the 104 pairs still given up on include them.

## Scoreboard
Unchanged. The README's rows for this pair rest on its latest `full` SUMMARY, and this report compares two
runs made side by side on four threads and writes no SUMMARY, as `2026-10-05-parallel-verify.md` did not.
