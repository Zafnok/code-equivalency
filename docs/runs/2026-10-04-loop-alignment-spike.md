# P1-023 loop-alignment spike: the three large runs (2026-10-04)

Question: of the 89 `unaligned-loop` Unknowns of the three large runs, how many have a pairing of
loop iterations that runs of the two sides show (unrolled, batched, peeled), which P1-024 would
propose from those runs and then prove?

**Answer: none. 0 of 89 have a fitting schedule other than 1:1 with no offset.** 47 (52.8%) fit
plain lockstep, which rung 2 already tries, and 42 fit no schedule in the ticket's bounds. Criterion
4's bar is 10, so P1-024 is closed with a measured line in ROADMAP's post-MVP list.

The result the ticket did not ask for is the larger one: `unaligned-loop` on these runs does not
mean the loops run out of step. In 67 of the 89 results rung 2 aligned the loops and then failed an
obligation, and in the 47 that fit lockstep the two sides made equal calls and ended with equal
outcomes on every one of 200 runs.

## Setup
- Runs (all `full`, default config, `tools/corpus/pairs.csv`):
  - `gitextensions-8522`, legacy 3f4ed21998af, modern 5190ba5c1a5f, run `20261002-1814-full-after`:
    44 `unaligned-loop` results.
  - `gitextensions-9860`, legacy bcd0c2617bdd, modern 37797ea4dd74, run `20261003-0055-full`: 16.
  - `jellyfin-13023`, legacy 5e8c0fe40c0e, modern ceb850c77052, run `20261003-0055-full`: 29.
- 89 results are 83 distinct procedure identities: six procedures of Git Extensions are in both of
  its runs. Shares below are over the 89 results and over the 2,246 changed pairs of the three runs.
- equiv: `main` at `a74f2e0`, win-x64, loaded through the production frontend with the default
  config. The runs were made at earlier commits, so one result no longer has a loop on either side.
- Tool: `tools/spikes/loop-alignment/` (about 590 lines of C#). Each pair takes 130 s to 340 s,
  almost all of it loading and lowering. `--self-test` passes: a counting loop that calls once per
  iteration fits 1:1 against itself, 2:1 against its unrolling (1:2 the other way round), and 1:1
  with an offset of one legacy iteration against the same loop with one iteration peeled.
- The run was made three times while the causes were refined. The schedule column was the same
  each time.

## Method
**Structure.** Each pair's shared fragments become calls, as the ladder does before any rung
(`ProductEncoder.ShareFragments`). The spike records the lockstep rung's detail from the run's
`ladderTrace`, `LockstepInduction.Misalignment` at this commit, both loop forests, and whether a
loop body of either side holds a call, an `IrPure` or an opaque, as lowered. A pair whose forests
differ gets its cause from the structure and is not run.

**Runs.** Each side gets a marker call at the top of every loop header and runs in `IrInterpreter`
on 200 inputs, drawn as `TraceInvariantProposer` draws them (a bitvector in [-4, 20], one of three
elements of a sort, a map with a few entries). The interpreter's call trace then gives each header
visit and the call events between visits. One oracle answers both sides: a call by its callee and
the number of real calls the side made before it, a pure function by its function and arguments. A
call throws one time in sixteen, and a call's Boolean answer is true one, two or three times in
four, by run, so that both a `MoveNext` loop and an end-of-input loop go round. A run is usable when
both sides return or throw; one that reaches an opaque or spends 20,000 steps is dropped.

**Schedules.** "After `a` legacy and `b` modern iterations of one loop, every `m` legacy iterations
pair with `n` modern ones", `a`, `b` in 0..2 and `m`, `n` in 1..4, on each loop of the forest in
turn with every other loop paired visit for visit; simplest first (by `m + n`, then `a + b`). A
schedule fits when on every usable run both sides reach the same paired points in the same order,
with equal call events (callee, arguments and the heap the call reads) on every stretch, and some
run exercises it: one whole paired stretch on both sides.

Two readings of the ticket were needed (both are `Decision:` lines in its Notes):
- "Both sides leave their loops at a paired point" is read as "after the same paired point". An
  unrolled body keeps its exit tests, so on an odd trip count it leaves in the middle of a stretch;
  P1-024's rewrite allows exactly that. Read strictly, the ticket's own 2:1 self-test pair would
  not fit.
- A schedule that no run exercises does not fit, or every schedule would fit a pair whose loop no
  run enters.

**Diagnostic for the pairs with no schedule.** What first differs when the two sides are paired
visit for visit, on the most runs; and whether a schedule fits when two call events are equal as
soon as their callees are.

On the citations in the ticket: both exist as cited (Churchill, Padon, Sharma and Aiken, "Semantic
Program Alignment for Equivalence Checking", PLDI 2019; Shemer, Gurfinkel, Shoham and Vizel,
"Property Directed Self Composition", CAV 2019). Only the first finds its alignment from runs. The
second searches for the composition together with the invariant, over predicates the user gives.

## Pairs with a fitting schedule, by schedule

| Schedule | Results | Share of the 89 | Share of changed pairs |
|---|---|---|---|
| 1:1, no offset (lockstep) | 47 | 52.8% | 2.1% |
| any other | **0** | 0.0% | 0.0% |

By run: 24 of 44 on gitextensions-8522, 10 of 16 on gitextensions-9860, 13 of 29 on jellyfin-13023.
In all 47 the outcomes and the final by-ref values also agreed on every usable run.

## Pairs with none, by cause

| Cause | Results | Share of the 89 | Share of changed pairs |
|---|---|---|---|
| The call events differ (other) | 17 | 19.1% | 0.8% |
| No run that goes round the loop | 12 | 13.5% | 0.5% |
| An opaque in the loop or before it | 8 | 9.0% | 0.4% |
| Different number of loops | 2 | 2.2% | 0.1% |
| No run that reaches the loop | 2 | 2.2% | 0.1% |
| Neither side has a loop at this commit (other) | 1 | 1.1% | 0.0% |
| A loop on one side and a LINQ callee on the other | 0 | 0.0% | 0.0% |

- **The call events differ (17).** Runs go round the loop, and the two sides make different calls.
  What differs first, paired visit for visit: the heap a call reads in 11, the callee in 3, a
  call's arguments in 3. With call events compared by callee alone, 6 of the 17 fit lockstep and
  the other 11 still fit nothing. So in none of them is a schedule hidden behind an argument: the
  two sides either call the same callees in step with different data, or call different callees.
- **No run that goes round the loop (12).** Runs reach a header, the two sides agree on every run,
  and no run comes back to a header on both sides. The inputs and answers are random, and these
  loops are guarded by a condition random values do not meet.
- **An opaque (8).** Every run that would enter or go round the loop stops at an opaque node.
- **Different number of loops (2).** One side has a loop and the other none. Neither loop-free side
  calls a `System.Linq` member the other side does not, so P2-096's shape does not occur here.
- **No run reaches the loop (2).**

For the 22 pairs whose runs do not exercise the loop (12, 8 and 2), the runs say nothing about the
schedule.
That does not weaken criterion 4's line for P1-024 as designed: its proposer runs the same
interpreter on the same kind of inputs, so it would propose nothing for them either.

## What `unaligned-loop` was on these runs
The lockstep rung's detail in each run's `ladderTrace`, against what the spike found:

| Rung 2 in the run | Results | Lockstep fits | Call events differ | Runs do not exercise the loop | Structure |
|---|---|---|---|---|---|
| A step obligation fails | 47 | 37 | 2 | 8 | 0 |
| The loops do not align: the header states do not pair up | 20 | 1 | 13 | 6 | 0 |
| The base obligation fails | 19 | 9 | 2 | 8 | 0 |
| The loops do not align: different number of loops | 2 | 0 | 0 | 0 | 2 |
| No lockstep step (no loop at this commit) | 1 | 0 | 0 | 0 | 1 |

At `a74f2e0` rung 2 aligns 67 of the 89 (75.3%); 19 have header states that do not pair up, 2 a
different number of loops, and 1 is irreducible.

What the loop bodies hold (either side, as lowered): a call in 88 of 89, an `IrPure` as well in 25,
an opaque as well in 33. No pair has a loop without a call, so rung 4 applies to none of them, as
the ticket's Goal says.

| Loop bodies hold | Results | Lockstep fits | No schedule |
|---|---|---|---|
| call | 44 | 29 | 15 |
| call and opaque | 19 | 5 | 14 |
| call, pure and opaque | 14 | 7 | 7 |
| call and pure | 11 | 6 | 5 |
| nothing (no loop at this commit) | 1 | 0 | 1 |

## Reading
- **P1-024.** The schedules it would add (unrolled, batched, peeled, within `a`, `b` in 0..2 and
  `m`, `n` in 1..4) occur in none of the 89. These migrations and upgrades did not restructure
  loops. P1-024 is closed unbuilt (criterion 4).
- **The reason name misleads.** `unaligned-loop` is reported when rung 2 does not apply and also
  when one of its obligations has a model (`LoopLadder.Failed`). 66 of the 89 are the second kind:
  the loops align and the induction does not go through.
- **Where the 47 are stuck.** The two sides run in lockstep, make equal calls and end equal on
  every run, and rung 2's step (37) or base (9) obligation still has a model; one more now aligns
  and did not in its run. A step model starts from header states that are equal, which is all
  rung 2 assumes. So what these pairs lack is a stronger relation at the header than equal paired
  state, or a pairing of state that the names do not give, and not a schedule. That is the
  direction of rung 3 and of P1-009's mined relations, on loops that make calls. It is the largest
  group this spike found: 47 results, 2.1% of the changed pairs. It is not ticketed here.
- **The 20 whose header states do not pair up.** 13 of them make different calls on the two sides
  in the interpreter, so a better pairing of state would not prove them.

## Limits
- The oracle's answers are random, so a loop behind a condition that random values rarely meet is
  not exercised: 22 pairs. A schedule there would be found only from better inputs, which P1-024's
  proposer would not have either.
- A fit is evidence from at most 200 runs, not a proof. Nothing here was given to Z3 (size guard).
- A schedule is searched on one loop at a time, with the same forest on both sides. A batched loop
  with a remainder loop has a different number of loops and is counted under that cause; there are
  2 such pairs and both have a loop on one side only.

## Every result
"Usable" is the runs on which both sides returned or threw, "reaching" those on which a header was
visited, and "most visits" the most header visits of one side on one run.

| Run | Procedure | Loops (legacy, modern) | Loop bodies hold | Schedule or cause | Usable | Reaching | Most visits |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | `BugReporterTests.SerializableExceptionTests::Sanitize(string)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 92 | 13 |
| gitextensions-8522 | `CommonTestUtils.GitModuleTestHelper::Dispose()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 8 | 6 |
| gitextensions-8522 | `GitCommands.CommitMessageManager::FormatCommitMessage(string,bool,bool)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 82 | 12 |
| gitextensions-8522 | `GitCommands.CustomDiffMergeToolCache::ParseCustomDiffMergeTool(string,string)` | 1 (.), 1 (.) | call+pure | 1:1, no offset | 200 | 179 | 5 |
| gitextensions-8522 | `GitCommands.DiffMergeTools.VsDiffMerge::GetVsDiffMergePath()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 94 | 4 |
| gitextensions-8522 | `GitCommands.Git.GitBranchNameNormaliser::Rule01(string,GitCommands.Git.GitBranchNameOptions)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 85 | 7 |
| gitextensions-8522 | `GitCommands.GitModule::GetAllBranchesWhichContainGivenCommit(GitUIPluginInterfaces.ObjectId,bool,bool)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 16 | 15 |
| gitextensions-8522 | `GitCommands.GitModule::GetRebasePatchFiles()` | 2 (. 0), 2 (. 0) | call+pure+opaque | 1:1, no offset | 200 | 150 | 15 |
| gitextensions-8522 | `GitCommands.GitModule::IsRunningGitProcess()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 1 | 5 |
| gitextensions-8522 | `GitCommandsTests.StreamExtensionsTests::ReadNullTerminatedLines(byte[],byte[][])` | 2 (. 0), 2 (. 0) | call+opaque | 1:1, no offset | 195 | 177 | 5 |
| gitextensions-8522 | `GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[])` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 159 | 8 |
| gitextensions-8522 | `GitExtUtilsTests.LazyStringSplitTests::RemoveEmptyEntries(string,char,string[])` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 159 | 8 |
| gitextensions-8522 | `GitExtensions.Plugins.CreateLocalBranches.CreateLocalBranchesForm::button1_Click(object,System.EventArgs)` | 1 (.), 1 (.) | call+pure+opaque | 1:1, no offset | 200 | 9 | 16 |
| gitextensions-8522 | `GitUI.AutoCompletion.CommitAutoCompleteProvider::ParseRegexes()` | 2 (. 0), 2 (. 0) | call+pure | 1:1, no offset | 200 | 79 | 5 |
| gitextensions-8522 | `GitUI.BranchTreePanel.RepoObjectsTree.SubmoduleTree::CreateSubmoduleNodes(GitCommands.Submodules.SubmoduleInfoResult,GitCommands.GitModule,ref System.Collections.Generic.List<global::GitUI.BranchTreePanel.RepoObjectsTree.SubmoduleNode>)` | 2 (. .), 2 (. .) | call+pure+opaque | 1:1, no offset | 200 | 88 | 5 |
| gitextensions-8522 | `GitUI.CommandsDialogs.FormBrowse::UpdateSubmoduleMenuStatusAsync(GitCommands.Submodules.SubmoduleInfoResult,System.Threading.CancellationToken)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 3 | 2 |
| gitextensions-8522 | `GitUI.CommandsDialogs.FormResolveConflicts::customMergetool_Click(object,System.EventArgs)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 58 | 7 |
| gitextensions-8522 | `GitUI.Editor.FileViewerInternal::GetTextMarkersMatchingWord(string)` | 1 (.), 1 (.) | call+pure | 1:1, no offset | 200 | 15 | 2 |
| gitextensions-8522 | `GitUI.RevisionGridControl::OnGridViewDragDrop(object,System.Windows.Forms.DragEventArgs)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 7 | 2 |
| gitextensions-8522 | `GitUI.Script.ScriptOptionsParser::Parse(string,GitUIPluginInterfaces.IGitModule,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl)` | 1 (.), 1 (.) | call+opaque | 1:1, no offset | 200 | 26 | 2 |
| gitextensions-8522 | `GitUIPluginInterfaces.PluginsPathScanner::GetFiles(string[])` | 2 (. 0), 2 (. 0) | call | 1:1, no offset | 200 | 190 | 19 |
| gitextensions-8522 | `ICSharpCode.TextEditor.Actions.ToggleLineComment::ShouldComment(ICSharpCode.TextEditor.Document.IDocument,string,ICSharpCode.TextEditor.Document.ISelection,int,int)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 200 | 2 |
| gitextensions-8522 | `ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::ScanDirectory(string)` | 2 (. 0), 2 (. 0) | call+pure+opaque | 1:1, no offset | 200 | 188 | 11 |
| gitextensions-8522 | `ResourceManager.Translator::GetAllTranslations()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 82 | 14 |
| gitextensions-9860 | `BugReporterTests.SerializableExceptionTests::Sanitize(string)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 92 | 13 |
| gitextensions-9860 | `GitCommands.Git.GetAllChangedFilesOutputParser::GetAllChangedFilesFromString_v2(string)` | 1 (.), 1 (.) | call+pure | 1:1, no offset | 200 | 44 | 19 |
| gitextensions-9860 | `GitCommands.Git.GitBranchNameNormaliser::Rule04(string,GitCommands.Git.GitBranchNameOptions)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 90 | 11 |
| gitextensions-9860 | `GitCommands.GitModule::IsRunningGitProcess()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 1 | 5 |
| gitextensions-9860 | `GitCommands.Patches.PatchManager::CorrectHeaderForNewFile(string)` | 2 (. .), 2 (. .) | call | 1:1, no offset | 200 | 99 | 18 |
| gitextensions-9860 | `GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[])` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 159 | 8 |
| gitextensions-9860 | `GitExtUtilsTests.LazyStringSplitTests::RemoveEmptyEntries(string,char,string[])` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 159 | 8 |
| gitextensions-9860 | `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::ValidateOrder(System.Collections.Generic.List<global::System.Windows.Forms.TreeNode>,System.Windows.Forms.TreeNodeCollection,int[])` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 78 | 2 |
| gitextensions-9860 | `GitUI.CommandsDialogs.SettingsDialog.Pages.FormBrowseRepoSettingsPage::SettingsToPage()` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 2 | 2 |
| gitextensions-9860 | `ICSharpCode.TextEditor.Document.FileSyntaxModeProvider::ScanDirectory(string)` | 2 (. 0), 2 (. 0) | call+pure+opaque | 1:1, no offset | 200 | 188 | 11 |
| jellyfin-13023 | `Emby.Naming.TV.SeasonPathParser::GetSeasonNumberFromPath(string,bool,bool)` | 2 (. .), 2 (. .) | call+pure | 1:1, no offset | 200 | 47 | 4 |
| jellyfin-13023 | `Emby.Server.Implementations.Data.SqliteItemRepository::AddItem(System.Collections.Generic.List<global::MediaBrowser.Controller.Entities.BaseItem>,MediaBrowser.Controller.Entities.BaseItem)` | 2 (. 0), 2 (. 0) | call | 1:1, no offset | 200 | 200 | 17 |
| jellyfin-13023 | `Emby.Server.Implementations.Session.SessionManager::OnDeviceManagerDeviceOptionsUpdated(object,Jellyfin.Data.Events.GenericEventArgs<global::System.Tuple<string, global::Jellyfin.Data.Entities.Security.DeviceOptions>>)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 86 | 3 |
| jellyfin-13023 | `Emby.Server.Implementations.Session.SessionManager::RevokeUserTokens(System.Guid,string)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 21 | 4 |
| jellyfin-13023 | `Jellyfin.Server.Filters.ParameterObsoleteFilter::Apply(Microsoft.OpenApi.Models.OpenApiOperation,Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext)` | 2 (. 0), 2 (. 0) | call | 1:1, no offset | 200 | 18 | 2 |
| jellyfin-13023 | `MediaBrowser.LocalMetadata.Parsers.BaseItemXmlParser`1::GetShare(System.Xml.XmlReader)` | 1 (.), 1 (.) | call+pure+opaque | 1:1, no offset | 200 | 84 | 4 |
| jellyfin-13023 | `MediaBrowser.MediaEncoding.Probing.ProbeResultNormalizer::NormalizeFormat(string,System.Collections.Generic.IReadOnlyList<global::MediaBrowser.Model.Entities.MediaStream>)` | 1 (.), 1 (.) | call+pure | 1:1, no offset | 200 | 39 | 10 |
| jellyfin-13023 | `MediaBrowser.MediaEncoding.Probing.ProbeResultNormalizer::SplitDistinctArtists(string,char[],bool)` | 1 (.), 1 (.) | call | 1:1, no offset | 200 | 59 | 5 |
| jellyfin-13023 | `MediaBrowser.Model.Dlna.StreamBuilder::GetDefaultSubtitleStreamIndex(MediaBrowser.Model.Dto.MediaSourceInfo,MediaBrowser.Model.Dlna.SubtitleProfile[])` | 4 (. . . 2), 4 (. . . 2) | call+opaque | 1:1, no offset | 200 | 55 | 4 |
| jellyfin-13023 | `MediaBrowser.Model.Dlna.StreamBuilder::GetOptimalAudioStream(MediaBrowser.Model.Dlna.MediaOptions)` | 1 (.), 1 (.) | call+opaque | 1:1, no offset | 200 | 101 | 10 |
| jellyfin-13023 | `MediaBrowser.Model.Dlna.StreamBuilder::GetSubtitleProfile(MediaBrowser.Model.Dto.MediaSourceInfo,MediaBrowser.Model.Entities.MediaStream,MediaBrowser.Model.Dlna.SubtitleProfile[],MediaBrowser.Model.Session.PlayMethod,MediaBrowser.Model.Dlna.ITranscoderSupport,string,Jellyfin.Data.Enums.MediaStreamProtocol?)` | 2 (. .), 2 (. .) | call+opaque | 1:1, no offset | 200 | 32 | 22 |
| jellyfin-13023 | `MediaBrowser.Model.Extensions.ContainerHelper::ContainsContainer(System.Collections.Generic.IReadOnlyList<string>,bool,string)` | 2 (. 0), 2 (. 0) | call | 1:1, no offset | 200 | 89 | 4 |
| jellyfin-13023 | `MediaBrowser.Providers.Manager.ItemImageProvider::MergeImages(MediaBrowser.Controller.Entities.BaseItem,System.Collections.Generic.IReadOnlyList<global::MediaBrowser.Controller.Providers.LocalImageInfo>,MediaBrowser.Controller.Providers.ImageRefreshOptions)` | 1 (.), 1 (.) | call+pure+opaque | 1:1, no offset | 200 | 103 | 5 |
| gitextensions-8522 | `GitUI.Blame.BlameControl::BuildBlameContents(string,int)` | 1 (.), 1 (.) | call+opaque | an opaque in the loop or before it | 199 | 1 | 1 |
| gitextensions-8522 | `GitUI.CommandsDialogs.FormCommit::Stage(System.Collections.Generic.IReadOnlyList<global::GitCommands.GitItemStatus>)` | 4 (. . . .), 4 (. . . .) | call+opaque | an opaque in the loop or before it | 24 | 1 | 1 |
| gitextensions-8522 | `NetSpell.SpellChecker.Spelling::ReplaceChars(System.Collections.Generic.List<global::NetSpell.SpellChecker.Dictionary.Word>)` | 2 (. 0), 2 (. 0) | call+opaque | an opaque in the loop or before it | 194 | 82 | 1 |
| gitextensions-9860 | `GitUI.FileStatusList::SelectPreviousVisibleItem()` | 2 (. 0), 2 (. 0) | call+opaque | an opaque in the loop or before it | 194 | 6 | 1 |
| jellyfin-13023 | `Emby.Server.Implementations.MediaEncoder.EncodingManager::RefreshChapterImages(MediaBrowser.Controller.Entities.Video,MediaBrowser.Controller.Providers.IDirectoryService,System.Collections.Generic.IReadOnlyList<global::MediaBrowser.Model.Entities.ChapterInfo>,bool,bool,System.Threading.CancellationToken)` | 1 (.), 1 (.) | call+pure+opaque | an opaque in the loop or before it | 160 | 0 | 0 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::EnforceBindSettings(MediaBrowser.Common.Net.NetworkConfiguration)` | 1 (.), 1 (.) | call+opaque | an opaque in the loop or before it | 182 | 0 | 0 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::GetAllBindInterfaces(bool)` | 1 (.), 1 (.) | call+opaque | an opaque in the loop or before it | 186 | 0 | 0 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::GetBindAddress(System.Net.IPAddress,out int?,bool)` | 1 (.), 1 (.) | call | an opaque in the loop or before it | 0 | 0 | 0 |
| gitextensions-8522 | `GitUI.PluginRegistry::Initialize()` | 1 (.), 0 () | call | different number of loops | 0 | 0 | 0 |
| jellyfin-13023 | `System.Text.RegularExpressions.Generated.KeyValueRegex_1.RunnerFactory.Runner::TryMatchAtCurrentPosition(System.ReadOnlySpan<char>)` | 1 (.), 0 () | call | different number of loops | 0 | 0 | 0 |
| gitextensions-8522 | `GitCommands.Submodules.SubmoduleStatusProvider::SetSubmoduleData(GitCommands.GitModule,GitCommands.Submodules.SubmoduleInfoResult,string,GitUIPluginInterfaces.IGitModule)` | 1 (.), 1 (.) | call+pure | no run that goes round the loop | 200 | 7 | 1 |
| gitextensions-8522 | `GitExtensions.Plugins.GitImpact.ImpactLoader::LoadModuleInfo(string,GitUIPluginInterfaces.IGitModule,System.Threading.CancellationToken)` | 2 (. 0), 2 (. 0) | call+pure+opaque | no run that goes round the loop | 200 | 37 | 1 |
| gitextensions-8522 | `GitUI.CommandsDialogs.FormBrowse::OpenContainingFolder(GitUI.FileStatusList,GitCommands.GitModule)` | 1 (.), 1 (.) | call | no run that goes round the loop | 200 | 20 | 1 |
| gitextensions-8522 | `GitUI.CommandsDialogs.RevisionFileTreeControl::findToolStripMenuItem_Click(object,System.EventArgs)` | 1 (.), 1 (.) | call+opaque | no run that goes round the loop | 200 | 2 | 1 |
| gitextensions-8522 | `GitUI.CommandsDialogs.SettingsDialog.Pages.FormBrowseRepoSettingsPage::SettingsToPage()` | 1 (.), 1 (.) | call | no run that goes round the loop | 200 | 4 | 1 |
| gitextensions-8522 | `GitUI.RevisionGridControl::OnGridViewDragEnter(object,System.Windows.Forms.DragEventArgs)` | 1 (.), 1 (.) | call | no run that goes round the loop | 200 | 8 | 1 |
| gitextensions-8522 | `ICSharpCode.TextEditor.Document.DefaultFormattingStrategy::SmartReplaceLine(ICSharpCode.TextEditor.Document.IDocument,ICSharpCode.TextEditor.Document.LineSegment,string)` | 1 (.), 1 (.) | call+pure | no run that goes round the loop | 200 | 1 | 1 |
| gitextensions-8522 | `ICSharpCode.TextEditor.Document.DefaultHighlightingStrategy::MatchExpr(ICSharpCode.TextEditor.Document.LineSegment,char[],int,ICSharpCode.TextEditor.Document.IDocument,bool)` | 5 (. 0 0 0 0), 5 (. 0 0 0 0) | call | no run that goes round the loop | 200 | 200 | 1 |
| gitextensions-9860 | `GitCommands.Git.GetAllChangedFilesOutputParser::GetAllChangedFilesFromString_v1(string,bool,GitCommands.StagedStatus)` | 1 (.), 1 (.) | call+pure+opaque | no run that goes round the loop | 200 | 11 | 1 |
| gitextensions-9860 | `GitCommands.GitModule::GetTagMessage(string)` | 1 (.), 1 (.) | call | no run that goes round the loop | 200 | 2 | 1 |
| gitextensions-9860 | `GitCommands.Patches.PatchManager::GetSelectedChunks(string,int,int,out string)` | 1 (.), 1 (.) | call+pure+opaque | no run that goes round the loop | 200 | 21 | 1 |
| jellyfin-13023 | `MediaBrowser.Controller.Entities.BaseItem::CreateSortName()` | 3 (. . .), 3 (. . .) | call+pure | no run that goes round the loop | 200 | 4 | 3 |
| gitextensions-9860 | `BugReporter.ExceptionDetails::Initialize(BugReporter.Serialization.SerializableException)` | 1 (.), 1 (.) | call | no run that reaches the loop | 200 | 0 | 0 |
| jellyfin-13023 | `GET /videos/{itemid}/hls/{playlistid}/{segmentid}.{segmentcontainer}` | 1 (.), 1 (.) | call | no run that reaches the loop | 200 | 0 | 0 |
| jellyfin-13023 | `System.Text.RegularExpressions.Generated.FqdnGeneratedRegex_1.RunnerFactory.Runner::TryMatchAtCurrentPosition(System.ReadOnlySpan<char>)` | 0 (), 0 () | nothing | neither side has a loop at this commit | 0 | 0 | 0 |
| gitextensions-8522 | `BugReporter.Program::Main()` | 1 (.), 1 (.) | call+opaque | the call events differ, first in a call's arguments; by callee alone: 1:1, no offset | 181 | 38 | 21 |
| gitextensions-9860 | `GitUITests.TranslationTest::CreateInstanceOfClass()` | 2 (. 0), 2 (. 0) | call+opaque | the call events differ, first in a call's arguments; by callee alone: 1:1, no offset | 196 | 32 | 3 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::MatchesExternalInterface(System.Net.IPAddress,out string)` | 1 (.), 1 (.) | call | the call events differ, first in a call's arguments; by callee alone: 1:1, no offset | 200 | 75 | 4 |
| gitextensions-8522 | `GitExtensions.Plugins.Gource.GourceStart::LoadAvatarsAsync()` | 1 (.), 1 (.) | call | the call events differ, first in the callee; by callee alone: none | 197 | 154 | 20 |
| gitextensions-8522 | `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::GetLink(System.Windows.Forms.RichTextBox,int)` | 3 (. . .), 3 (. . .) | call+pure | the call events differ, first in the callee; by callee alone: none | 200 | 7 | 4 |
| gitextensions-8522 | `ResourceManager.Xliff.TranslationUtil::GetTranslatableTypes()` | 2 (. 0), 2 (. 0) | call | the call events differ, first in the callee; by callee alone: none | 200 | 86 | 10 |
| gitextensions-8522 | `GitUI.CommitInfo.RefsFormatter::FilterAndFormatBranches(System.Collections.Generic.IEnumerable<string>,bool,bool)` | 1 (.), 1 (.) | call+pure | the call events differ, first in the heap a call reads; by callee alone: 1:1, no offset | 200 | 71 | 8 |
| gitextensions-8522 | `GitUI.GitUICommands::InitializeArguments(System.Collections.Generic.IReadOnlyList<string>)` | 1 (.), 1 (.) | call+opaque | the call events differ, first in the heap a call reads; by callee alone: 1:1, no offset | 186 | 186 | 5 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::UpdateSettings(object)` | 1 (.), 1 (.) | call+pure+opaque | the call events differ, first in the heap a call reads; by callee alone: 1:1, no offset | 200 | 4 | 2 |
| gitextensions-8522 | `GitUI.CommandsDialogs.FormCommit::generateListOfChangesInSubmodulesChangesToolStripMenuItem_Click(object,System.EventArgs)` | 1 (.), 1 (.) | call+pure+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 188 | 4 | 1 |
| gitextensions-8522 | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::BuildOrderedRowCache(GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision[],int,int)` | 5 (. 0 1 0 0), 5 (. 0 1 0 0) | call+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 199 | 22 | 1 |
| jellyfin-13023 | `Jellyfin.Api.Helpers.DynamicHlsHelper::AddSubtitles(MediaBrowser.Controller.Streaming.StreamState,System.Collections.Generic.IEnumerable<global::MediaBrowser.Model.Entities.MediaStream>,System.Text.StringBuilder,System.Security.Claims.ClaimsPrincipal)` | 1 (.), 1 (.) | call+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 198 | 19 | 1 |
| jellyfin-13023 | `Jellyfin.Api.Helpers.DynamicHlsHelper::AddTrickplay(MediaBrowser.Controller.Streaming.StreamState,System.Collections.Generic.Dictionary<int, global::Jellyfin.Data.Entities.TrickplayInfo>,System.Text.StringBuilder,System.Security.Claims.ClaimsPrincipal)` | 1 (.), 1 (.) | call+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 193 | 90 | 1 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::InitializeInterfaces()` | 2 (. 0), 2 (. 0) | call+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 31 | 6 | 1 |
| jellyfin-13023 | `Jellyfin.Networking.Manager.NetworkManager::MatchesPublishedServerUrl(System.Net.IPAddress,bool,out string)` | 1 (.), 1 (.) | call+pure+opaque | the call events differ, first in the heap a call reads; by callee alone: none | 168 | 52 | 1 |
| jellyfin-13023 | `System.Text.RegularExpressions.Generated.CodecRegex_2.RunnerFactory.Runner::TryFindNextPossibleStartingPosition(System.ReadOnlySpan<char>)` | 1 (.), 1 (.) | call | the call events differ, first in the heap a call reads; by callee alone: none | 200 | 22 | 2 |
| jellyfin-13023 | `System.Text.RegularExpressions.Generated.FilterRegex_3.RunnerFactory.Runner::TryFindNextPossibleStartingPosition(System.ReadOnlySpan<char>)` | 1 (.), 1 (.) | call | the call events differ, first in the heap a call reads; by callee alone: none | 200 | 26 | 2 |
