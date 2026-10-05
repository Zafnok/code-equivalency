# P2-100 solver repeatability: gitextensions-8522 (2026-10-04)

Question: with every check run in a Z3 context of its own (ticket P2-100), does the same pair under the
same resource limit end the same way on every run, on four threads and on one?

**Answer: yes, for every pair the resource limit ends or that is decided. Of the 184 pairs of P2-050's
measurement, 153 were ended by the resource limit or decided in all four runs, and all 153 have the same
outcome, ladder and detail in all four.** The other 31 met the wall-clock backstop in at least one run and
are listed apart below. On each of them, the runs that did not meet the backstop agree with each other, and
no pair's outcome differs between any two runs.

P2-050's two runs of the same pairs (`docs/runs/2026-10-01-timeout-budget.md`, "Repeatability") left one
pair that the same resource limit ended in one run and that found a model in the other. No such pair is
left here.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- Input: the 184 pairs of P2-050's repeatability measurement, by procedure identity. All 184 still match a
  pair with both bodies; 13,541 pairs loaded, no project skipped, no lowering failure.
- What was run: P2-050's throwaway harness (not committed), which loads both solutions through the
  production frontend and hands each pair's two lowered bodies to `Z3Backend.Verify` with the default
  bound, `resourceLimit` 5,000,000 and `timeoutMs` 60,000. Nothing after the backend ran. The ladder is
  compared as each step's rung, outcome and detail, and the verdict's detail for every Unknown.
- Four runs, A and B on four threads and C and D on one thread, in one process after one load of the
  solutions, all four at the same time (ten solver threads).
- equiv: this ticket's branch at c3547b9a, on `main` 30af7c87.
- Machine: one Windows box, 24 logical cores, with nothing else of weight running (2026-10-05). A first
  measurement the night before shared the machine with other work at 100% CPU; it is under "The same
  measurement on a loaded machine".

## Results

| run | threads | Equivalent | Divergent | timeout | other Unknown | pairs that met the backstop | sum of pair seconds | wall-clock |
|---|---|---|---|---|---|---|---|---|
| A | 4 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 30 | 4,977 | 1,451 s |
| B | 4 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 29 | 4,990 | 1,451 s |
| C | 1 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 22 | 4,355 | 4,357 s |
| D | 1 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 22 | 4,380 | 4,382 s |

| | pairs |
|---|---|
| ended by the resource limit, or decided, in all four runs | 153 |
| of those, the same outcome, ladder and detail in all four | 153 |
| met the wall-clock backstop in at least one run | 31 |
| of those, in all four runs | 21 |
| of those, only in the four-thread runs | 9 (8 in both, 1 in A alone) |
| of those, only in the one-thread runs | 1 (in both) |
| of those, on which the runs that did not meet it differ from each other | 0 |
| whose outcome differs between any two runs | 0 |

The 153 are 63 Equivalent, 72 timeout on the resource limit, 13 Unknown(opaque), 4 Unknown(abstraction) and
1 Divergent.

- The counts are not P2-050's (0 Equivalent and 173 or 174 timeouts on the same pairs). `main` has moved
  since: the pair's `timeout` Unknowns were 206 on 2026-09-30, 164 in P2-076's run and 138 on 2026-10-04
  (`docs/runs/2026-10-04-cvc5-budget.md`, `docs/runs/2026-10-04-failure-refinement.md`). These runs did not
  measure how much of the difference, if any, comes from this change.
- Time. The one-thread runs took 4,355 and 4,380 pair seconds; P2-050's four-thread runs of the same pairs
  took 4,438 and 4,377. The pairs do not do the same work as then, so this bounds nothing exactly, but it
  shows no large cost from translating each query into its own context. The four-thread runs here took 14%
  longer than the one-thread runs beside them.
- The backstop ended a query on 22 pairs in each one-thread run and on 30 and 29 in the four-thread runs,
  against 10 and 8 in P2-050's runs. Not explained here: the queries a pair asks have changed since (every
  Unknown now also asks the failure-refinement queries), and ten solver threads ran at once.
- The same pair still takes different times in two runs that end it the same way. Of the 153, 15 differ
  by more than 1.5 times between the two one-thread runs (at most 3.75 times) and 7 between the two
  four-thread runs (at most 2.4 times). The resource limit makes the outcome repeat, not the time.

## The pairs the wall-clock backstop ended, listed apart
31 pairs: the backstop ended at least one query of the pair in the runs named. Their time is P2-076's.

| pair | runs that met the backstop |
|---|---|
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | all four |
| `GitCommands.ExecutableExtensions::ExecuteAsync(GitUIPluginInterfaces.IExecutable,GitExtUtils.ArgumentString,System.Action<global::System.IO.StreamWriter>,System.Text.Encoding,GitCommands.CommandCache,bool)` | all four |
| `GitCommands.Patches.Chunk::ParseChunk(string,int,int,int)` | all four |
| `GitCommands.Patches.PatchProcessor::CreatePatchFromString(string[],System.Lazy<global::System.Text.Encoding>,ref int)` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_second_nested_module_changes()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change_commit()` | C, D |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_commit_second_nested_module_change()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_no_forced_changes()` | A, B |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_second_nested_module_change()` | all four |
| `GitExtensions.Plugins.CreateLocalBranches.CreateLocalBranchesForm::button1_Click(object,System.EventArgs)` | all four |
| `GitExtensions.Plugins.FindLargeFiles.FindLargeFilesForm::FindLargeFilesFunction()` | all four |
| `GitExtensions.Plugins.GitImpact.ImpactLoader::LoadModuleInfo(string,GitUIPluginInterfaces.IGitModule,System.Threading.CancellationToken)` | A, B |
| `GitExtensions.Plugins.Gource.GourcePlugin::Execute(GitUIPluginInterfaces.GitUIEventArgs)` | A |
| `GitUI.CommandsDialogs.FormBrowseMenus::CreateToolStripSubMenus(System.Windows.Forms.ToolStrip,System.Windows.Forms.ToolStripMenuItem)` | all four |
| `GitUI.CommandsDialogs.FormClone::OkClick(object,System.EventArgs)` | all four |
| `GitUI.CommandsDialogs.FormClone::OnRuntimeLoad(System.EventArgs)` | A, B |
| `GitUI.CommandsDialogs.FormClone::ToTextUpdate(object,System.EventArgs)` | A, B |
| `GitUI.CommandsDialogs.FormFormatPatch::FormatPatch_Click(object,System.EventArgs)` | all four |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ShellExtensionSettingsPage::InitializeComponent()` | all four |
| `GitUI.Editor.BlameAuthorMargin::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | all four |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | all four |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessTags(System.Windows.Forms.RichTextBox,System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<int, string>>,bool)` | all four |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::SetXHTMLText(System.Windows.Forms.RichTextBox,string)` | all four |
| `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | all four |
| `GitUI.Script.ScriptOptionsParser::ParseScriptArguments(string,string,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl,GitUIPluginInterfaces.IGitModule,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.GitRevision>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<string>,GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,GitUIPluginInterfaces.GitRevision,string)` | A, B |
| `GitUI.UserControls.EditboxBasedConsoleOutputControl::StartProcess(string,string,string,System.Collections.Generic.Dictionary<string, string>)` | A, B |
| `GitUI.WindowPositionManager::IsDisplayedOn10Percent(System.Drawing.Rectangle,System.Drawing.Rectangle)` | all four |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` | A, B |
| `ICSharpCode.TextEditor.Document.TextUtilities::GetExpressionBeforeOffset(ICSharpCode.TextEditor.TextArea,int)` | all four |
| `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Setup()` | A, B |

## The same measurement on a loaded machine
The night before (2026-10-04), the same four runs were made with the branch as it was before its rebase
(on `main` 2627bf9f), on the same machine at 100% CPU from other work.

| | loaded | quiet (above) |
|---|---|---|
| ended by the resource limit, or decided, in all four runs | 137 | 153 |
| of those, identical in all four | 137 | 153 |
| met the backstop in at least one run | 47 | 31 |
| pairs that met the backstop, per run (A, B, C, D) | 44, 44, 29, 24 | 30, 29, 22, 22 |
| sum of pair seconds (A, B, C, D) | 7,748, 7,664, 5,111, 5,078 | 4,977, 4,990, 4,355, 4,380 |
| pairs whose outcome differs between two runs | 1 (met the backstop in A) | 0 |

Load costs time and backstop hits and no repeatability: every pair that met the backstop in neither
measurement has the same outcome, ladder and detail in both, on two builds.

That evening's very first attempt started three harness processes at once, and each loaded a different part
of the two solutions (13,168, 13,460 and 7,057 of 13,541 pairs). Its runs are in neither column.

## Findings
- Every pair the resource limit ends, or that is decided, repeats: 153 of 153 over four runs, on four
  threads and on one, run side by side in one process; and 137 of 137 on a fully loaded machine.
- 31 of 184 pairs still meet the 60 s backstop on a quiet machine, 21 of them in every run. A result that
  ran into the backstop can differ between runs, as VERIFICATION-MODEL.md section 6 says; on the loaded
  machine one did. P2-076 owns the time those queries take.
- Several processes loading the same solutions at once each load a different part of them. Not
  investigated; no ticket yet.
