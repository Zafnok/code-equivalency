# P2-076 The time a pair spends outside its solver budget is measured and removed, and no pair is ended early
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: none

## Goal
The 2026-09-29 `full` run of `gitextensions-8522` (P2-046) took 8h57m. Six pairs took 5.4 of its 6.8
verify hours, and every one ended Unknown. Their `rung=bounded` lines report `result=timeout` against
a 5000 ms timeout after 126.5, 52.7, 25.6, 16.0, 12.8 and 9.7 minutes
(`GitUI.CommandsDialogs.FormRemotes::InitializeComponent()` is the 126.5). Two of the six also spend
most of their time outside any rung line: 50.0 of 75.6 minutes for `TextView::DrawDocumentWord`, and
21.9 of 34.7 for `Nodes::FillTreeViewNode`. So the time is not the solver using its budget. It goes
somewhere in building the product encoding, `Z3Backend.Inline`'s substitution, the tactic pipeline, a
query that overruns its `timeout`, or the work after the ladder (`ReachableOpaques`, failure
refinement), and nobody has measured which. Separately, after `verify done` at +6h50m the run spent
2h07m in the ADR 0036 contracts pass (`CompareCommand.WithContracts`), which writes rung lines but no
phase or items, so the progress log cannot show it.

Measure where the time goes and remove it, and log the contracts pass as its own phase. This ticket
adds **no new way to end a pair**. A pair that reaches a verdict today reaches the same verdict
afterwards. The same run shows why a cap is the wrong tool: an Equivalent pair's `bounded` rung took
16.1 s and a Divergent's whole ladder took 72.5 s, both legitimately, because a rung makes several
queries and each query has its own budget. A cap of a few times `timeoutMs` per rung or per pair
would have turned them Unknown.

## Spec references
ADR 0029 decision 5 (timeouts are per pair; no run-level budget produces Unknowns), ADR 0038 (phases
and items), ADR 0036 (contracts pass), `src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`, `Inline`,
`ReachableOpaques`), `src/Equiv.Verify.Z3/LoopLadder.cs` (rung timing), `src/Equiv.Verify.Z3/FailureRefinementQuery.cs`,
`src/Equiv.Cli/CompareCommand.cs` (`Verified`, `WithContracts`). P2-050 owns the solver budget itself
(`rlimit`, and whether a larger budget decides more pairs).

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. At `debug`, every pair's log shows its time by stage: encoding, `Inline`, each solver
   query (`Check`, with its result), and each step after the ladder. For the 30 pairs over 30 s in
   P2-046's run, record the stage totals in `## Notes` (identities and seconds only), and log a
   `Decision:` line naming the dominant stage or stages. Also record every single query in the run
   that returned `sat` or `unsat` after more than `timeoutMs`, with its time.
2. Work that is not a solver query (encoding, `Inline`, model decoding, bookkeeping) is made cheaper.
   It is never skipped, capped or abandoned. A pair's encoding and query set are the same before and
   after: a test asserts that the assertions handed to Z3 are unchanged for the fixtures in
   `tests/Equiv.Verify.Z3.Tests`. If a stage cannot be made cheaper without changing what is asked
   of the solver, stop and record it in `## Notes`. That is a finding for its own ticket.
3. A solver query that runs past `timeoutMs` may be interrupted (`Context.Interrupt`) only after a
   slack chosen from criterion 1's list: at least twice the longest time at which any query in the
   run returned `sat` or `unsat`, and never under 4 x `timeoutMs`. The slack is logged as a
   `Decision:` line. An interrupted query has the result a timeout has today, and the ladder goes on
   to the next rung exactly as it does after a timeout. No rung-level, pair-level or run-level cap
   is added.
4. The contracts pass is a `contracts` phase in the progress log (ADR 0038): one item per pair it
   re-verifies. `./tools/corpus/corpus.ps1 -Progress -Summary` lists it. It is subject to criteria
   1 to 3, and it re-verifies the same pairs as before.
5. No verdict is lost. Two `full` runs of `gitextensions-8522` (`equiv-corpus-run`), one at the
   commit this ticket branches from and one with the change, are compared result by result in
   `docs/runs/<date>-pair-time.md`:
   - every result that is Equivalent or Divergent before has the same rule id and `proofMethod` after;
   - every rung that returned `sat` or `unsat` before returns the same after;
   - a result may differ only by going from Unknown(timeout) to a decided verdict or to another
     Unknown reason, and each such pair is listed.
   If any decided result becomes Unknown, the ticket is not done. Remove the part that caused it.
   A result that two runs of the same build do not give alike (P2-082) is compared instead by how
   often repeated runs of each build decide it (see the Deviation in Notes).
   The report also gives verify and contracts seconds before and after, and the ten slowest pairs
   after.

## Files
`src/Equiv.Verify.Z3/` (the stages criterion 1 names, and the stage log lines),
`src/Equiv.Cli/CompareCommand.cs` (`WithContracts` phase), `tools/corpus/corpus.ps1` (`-Progress` only if
the new phase needs it), their tests, `docs/runs/<date>-pair-time.md`.

## Tests
`LadderFixtureTests.AssertionsAreUnchanged` (criterion 2), `LadderFixtureTests.InterruptedQueryIsATimeoutAndTheLadderContinues`
(criterion 3), `CompareCommandProgressTests.ContractsPassIsAPhase`, and one test per stage made
cheaper, named for it.

## Size guard
More than two stages changed, or any change to which queries a rung makes, means the ticket has
been misread: stop after criterion 1 and split it.

## Out of scope
The value of `timeoutMs`, `rlimit`, and whether the Unknown(timeout) pairs would decide with a larger
budget: all P2-050 (its criterion 1 reruns them at 1x, 4x and 20x). Verifying pairs in parallel
(P2-077). Any cap on a rung, a pair or a run.

## Notes
- Found 2026-09-30 while P1-018's runs were in progress, from the 2026-09-29 `full` run's
  `progress.log` (`.corpus/`, not committed).
- From that log, per rung line: `sat` 396 (longest 7.2 s), `unsat` 77 (longest 16.1 s, one rung of
  several queries), `timeout` 429 (185 over 5.5 s, 34 over 15 s, 13 over 60 s, longest 7,590 s),
  `unknown` 519 (longest 26.6 s). Decided pairs over 15 s in total: four (72.5 s and 62.8 s
  Divergent, where `bounded` timed out after 72.2 s and 55.5 s and `lockstep-induction` then
  answered; 27.7 s and 16.1 s Equivalent).
- Pairs over 120 s: 11, none decided. That is an observation about one run, not a licence for a cap.
- Decision: how a pair's time is logged -> one `stage=<name> took=<s>s` line per piece of work, none overlapping
  (`share`, `shape`, `unroll`, `encode`, `assert`, `inline`, `check:<query>` with `result=`, `replay`, `dispose`,
  `couple`, `encode-chc`, `propose`), and one `step=<name> took=<s>s` line for each step that is not a rung
  (`reachable-opaques`, `failure-refinement`, `contract-search`). A pair's time less its stages is bookkeeping.
  Alternatives: one summary line per pair, more fields on the rung line. Rule: 3.
- Decision: when the `contracts` phase exists -> only in a run that re-verifies at least one pair, as `execute`
  exists only under `--execute`. Alternatives: an empty phase in every run. Rule: 4.
- Decision: the weight of a `contracts` item -> the pair's `PairWeight`, as in `verify`. Alternatives: 1. Rule: 1.
- Decision: how `Inline` is made cheaper -> it hands Z3 the definitions whose constant the term holds, found by
  walking the term, and Z3's own substitution still builds the result. Alternatives: a substitution written here
  over the whole encoding; the inlined definitions cached per encoding. Rule: 1 (the same Z3 call on fewer pairs
  gives the same term, which `InlineTests` checks against the old substitution on every fixture).
- Decision: how context disposal is made cheaper -> `Inline` releases every Z3 object it made before it returns.
  Alternatives: forcing a garbage collection before the context is disposed; disposing contexts on another
  thread. Rule: 4.
- Decision: how a test reaches the interrupt -> `LoopLadder.InterruptAfterMs`, an init property that only a test
  sets, as `InvariantTimeoutMs` is. Alternatives: a public option; the test interrupting through the context
  factory. Rule: 4.
- Decision: the detail of an interrupted solver query -> Z3's own reason, `solver returned unknown (interrupted)`,
  with no limit named after it. Alternatives: a new suffix naming the slack. Rule: 1.
- Criterion 1, measured 2026-10-02 in a `full` run of `gitextensions-8522` at the base commit with the stage
  log added and nothing else changed (`docs/runs/2026-10-02-pair-time.md`). Stage totals in seconds of the 30
  pairs over 30 s in P2-046's run. `other` is the time no stage line covered: the context's disposal, which had
  no line yet, and bookkeeping. "After the ladder" is failure refinement, already counted in the stages.
  14 of the 30 are congruent on today's `main` and take 0 s: `GitUI.FileStatusList::UpdateFileStatusListView(bool)`, `GitUI.BranchTreePanel.RepoObjectsTree.Nodes::FillTreeViewNode(System.Windows.Forms.TreeNode)`, `GitUI.CommandsDialogs.FormBrowse::.ctor(GitUI.GitUICommands,string,GitUIPluginInterfaces.ObjectId,GitUIPluginInterfaces.ObjectId)`, `GitUI.CommandsDialogs.FormBrowse::InternalInitialize(bool)`, `GitUI.CommandsDialogs.FormFileHistory::.ctor(GitUI.GitUICommands,string,GitUIPluginInterfaces.GitRevision,bool,bool)`, `GitUI.CommandsDialogs.FormRemotes::InitializeComponent()`, `GitUI.CommandsDialogs.BrowseDialog.DashboardControl.UserRepositoriesList::InitializeComponent()`, `GitUI.CommitInfo.CommitInfo::InitializeComponent()`, `GitUI.Editor.FileViewer::ToHexDump(byte[],System.Text.StringBuilder,int,int)`, `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessTags(System.Windows.Forms.RichTextBox,System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<int, string>>,bool)`, `GitCommands.Git.AheadBehindDataProvider::GetData(System.Text.Encoding,string)`, `ResourceManager.Xliff.TranslationUtil::TranslateItemsFromList(string,ResourceManager.ITranslation,System.Collections.Generic.IEnumerable<(string name, object item)>)`, `ICSharpCode.TextEditor.TextView::PaintLinePart(System.Drawing.Graphics,int,int,int,System.Drawing.Rectangle,int)`, `ICSharpCode.TextEditor.TextView::DrawDocumentWord(System.Drawing.Graphics,string,System.Drawing.Point,System.Drawing.Font,System.Drawing.Color,System.Drawing.Brush)`.

  | pair | outcome | seconds | unroll and encode | inline | check | replay | other | of it after the ladder |
  |---|---|---|---|---|---|---|---|---|
  | `ICSharpCode.TextEditor.Document.DefaultHighlightingStrategy::ParseLine(ICSharpCode.TextEditor.Document.IDocument)` | unknown | 370.7 | 1.1 | 329.0 | 9.4 | 0.0 | 30.8 | 0.0 |
  | `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::BuildOrderedRowCache(GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision[],int,int)` | unknown | 277.2 | 0.6 | 204.8 | 20.2 | 0.0 | 50.4 | 220.3 |
  | `GitUI.CommandsDialogs.FormPush::PushChanges(System.Windows.Forms.IWin32Window)` | unknown | 250.8 | 2.3 | 177.1 | 62.8 | 0.0 | 8.6 | 0.0 |
  | `GitCommands.GitModule::GetRebasePatchFiles()` | unknown | 221.6 | 1.5 | 130.3 | 42.3 | 0.0 | 46.4 | 161.2 |
  | `TranslationApp.TranslationHelpers::LoadTranslation(System.Collections.Generic.IDictionary<string, global::ResourceManager.Xliff.TranslationFile>,System.Collections.Generic.IDictionary<string, global::System.Collections.Generic.List<global::TranslationApp.TranslationItemWithCategory>>)` | unknown | 193.4 | 0.9 | 9.3 | 110.8 | 0.0 | 72.4 | 121.7 |
  | `GitUI.CommandsDialogs.FormCommit::Stage(System.Collections.Generic.IReadOnlyList<global::GitCommands.GitItemStatus>)` | unknown | 133.8 | 0.4 | 81.1 | 35.0 | 0.0 | 17.0 | 110.4 |
  | `GitCommands.GitModule::ParseGitBlame(string,System.Text.Encoding)` | unknown | 119.8 | 1.0 | 72.6 | 36.4 | 0.2 | 9.5 | 51.3 |
  | `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()` | unknown | 119.0 | 0.6 | 82.9 | 6.9 | 0.0 | 26.8 | 0.0 |
  | `NetSpell.SpellChecker.Dictionary.WordDictionary::Initialize()` | unknown | 107.4 | 0.7 | 41.3 | 61.6 | 0.0 | 3.8 | 0.0 |
  | `GitUI.CommandsDialogs.FormCommit::generateListOfChangesInSubmodulesChangesToolStripMenuItem_Click(object,System.EventArgs)` | unknown | 88.5 | 0.2 | 4.8 | 69.0 | 0.0 | 14.6 | 24.9 |
  | `AppVeyorIntegration.AppVeyorAdapter::ExtractBuildInfo(string,string)` | divergent | 82.4 | 0.1 | 24.7 | 23.7 | 0.0 | 33.9 | 0.0 |
  | `GitUI.AutoCompletion.CommitAutoCompleteProvider::GetAutoCompleteWordsAsync(System.Threading.CancellationToken)` | unknown | 60.5 | 0.3 | 17.8 | 32.6 | 0.0 | 8.9 | 0.0 |
  | `GitCommands.ExternalLinks.ExternalLinkRevisionParser::ParseRemotes(GitCommands.ExternalLinks.ExternalLinkDefinition)` | unknown | 47.8 | 0.0 | 6.3 | 22.3 | 0.0 | 19.0 | 29.9 |
  | `GitCommands.Patches.Chunk::FromNewFile(GitCommands.GitModule,string,int,int,bool,byte[],System.Text.Encoding)` | unknown | 40.3 | 0.0 | 9.2 | 21.2 | 0.0 | 9.9 | 22.6 |
  | `GitUI.Blame.BlameControl::BuildBlameContents(string,int)` | unknown | 30.1 | 0.1 | 10.9 | 13.7 | 0.0 | 5.4 | 20.4 |
  | `ICSharpCode.TextEditor.Document.DefaultHighlightingStrategy::MatchExpr(ICSharpCode.TextEditor.Document.LineSegment,char[],int,ICSharpCode.TextEditor.Document.IDocument,bool)` | unknown | 26.5 | 0.2 | 8.5 | 11.8 | 0.0 | 6.1 | 9.2 |
  | total, 30 pairs | | 2169.9 | 10.1 | 1210.6 | 579.5 | 0.3 | 363.5 | 771.9 |

- Decision: the dominant stages -> `Inline` (1,211 of the 30 pairs' 2,170 s, 1,520 of the run's 7,016 `verify`
  seconds) and the disposal of the query's context (the 364 s and 774 s no stage line covered; the after runs'
  `dispose` lines show 40 s once it was cheap). Solver queries are the rest (580 s and 4,679 s), and their
  budget is P2-050's. Alternatives: encoding (19 s over the whole phase), model decoding (16 s), the steps
  after the ladder (their own queries). Rule: measured, criterion 1.
- Queries that returned sat or unsat after more than `timeoutMs` (60,000 ms) in that run: none. The longest
  sat took 13.5 s and the longest unsat 13.4 s, of 3,519 queries. No query ran past `timeoutMs` either: the
  longest unknown took 60.1 s.
- Decision: the interrupt's slack -> four times `timeoutMs`, 240 s at the default: the floor the ticket sets,
  and more than twice the longest sat or unsat answer (13.5 s; 22.4 s in the after runs). Alternatives: a fixed
  number of seconds, which a configured `timeoutMs` could undercut. Rule: 4.
- Deviation: criterion 5 cannot hold as written. It assumes a run gives the same result twice, but two `full`
  runs at the base commit, handing Z3 identical assertions, already differ on 7 of 13,742 results and lose 2
  Divergents to each other: queries at the edge of the resource limit end differently from run to run
  (P2-082, found after this ticket was written). The base run and the after run differ on 8 results, and
  every one of them, rerun ten times, gives different results from run to run with one build or the other.
  The twelve pairs that differ in any of the four runs are decided 22 times of 120 before and 28 after. One pair, `AmendState_should_be_false_if_file_contains`, is decided less often: 40 of
  200 runs at the base commit, 17 of 200 on this branch. The part that moves it is the new `Inline` (51 of
  200 with the old `Inline`, 19 of 200 with its objects left to the finalizer). The terms it hands Z3 are the
  same, so the cause is when the collector runs, which P2-082 is removing. Removing the new `Inline` would bring
  back 1,520 s of quadratic substitution that criterion 2 requires gone, and would trade this pair for others
  that the new `Inline` decides more often, such as `GitBranchNameNormaliser::Rule04`, decided 4 of 10 times
  before and 9 of 10 after. Two Unknowns move the same way without a verdict at stake:
  `GitModule::GetInteractiveRebasePatchFiles` (unaligned-loop 10 of 10 before, timeout 6 of 10 after) and
  `BlameControlTests::BuildBlameContents_WithDateAndTime` (opaque 10 of 10 before, timeout 5 of 10 after). So
  the new `Inline` stays, and criterion 5 is read as above: no decided result that the base commit gives in
  every run is lost, and each pair whose result differs between runs is listed in the report with its rate
  before and after.
