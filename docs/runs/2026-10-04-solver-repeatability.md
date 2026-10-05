# P2-100 solver repeatability: gitextensions-8522 (2026-10-04)

Question: with every check run in a Z3 context of its own (ticket P2-100), does the same pair under the
same resource limit end the same way on every run, on four threads and on one?

**Answer: yes, for every pair the resource limit ends or that is decided. Of the 184 pairs of P2-050's
measurement, 137 were ended by the resource limit or decided in all four runs, and all 137 have the same
outcome, ladder and detail in all four.** The other 47 met the wall-clock backstop in at least one run and
are listed apart below. On each of them, the runs that did not meet the backstop agree with each other.

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
- equiv: this ticket's branch at its first commit, on `main` 2627bf9f.
- Machine: one Windows box, 24 logical cores, at 100% CPU for the whole measurement: other work was running
  on it, and so was an earlier, mis-started attempt of this measurement (see "A first attempt"). A resource
  limit buys the same under that load. The wall-clock backstop buys less, which is why it ended far more
  queries here than in P2-050's runs (8 and 10 of 184 there).

## Results

| run | threads | Equivalent | Divergent | timeout | other Unknown | pairs that met the backstop | sum of pair seconds | wall-clock |
|---|---|---|---|---|---|---|---|---|
| A | 4 | 64 | 1 | 99 | 20 (opaque 13, abstraction 4, unaligned-loop 3) | 44 | 7,748 | 2,177 s |
| B | 4 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 44 | 7,664 | 2,190 s |
| C | 1 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 29 | 5,111 | 5,112 s |
| D | 1 | 64 | 1 | 98 | 21 (opaque 14, abstraction 4, unaligned-loop 3) | 24 | 5,078 | 5,079 s |

| | pairs |
|---|---|
| ended by the resource limit, or decided, in all four runs | 137 |
| of those, the same outcome, ladder and detail in all four | 137 |
| met the wall-clock backstop in at least one run | 47 |
| of those, in all four runs | 24 |
| of those, on which the runs that did not meet it differ from each other | 0 |

The 137 are 63 Equivalent, 57 timeout on the resource limit, 12 Unknown(opaque), 4 Unknown(abstraction) and
1 Divergent.

- The one pair whose outcome differs between runs (timeout in A, Unknown(opaque) in B, C and D) met the
  backstop in A. It is one of the 47.
- The counts are not P2-050's (0 Equivalent and 173 or 174 timeouts on the same pairs). `main` has moved
  since: the pair's `timeout` Unknowns were 206 on 2026-09-30, 164 in P2-076's run and 138 on 2026-10-04
  (`docs/runs/2026-10-04-cvc5-budget.md`, `docs/runs/2026-10-04-failure-refinement.md`). These runs did not
  measure how much of the difference, if any, comes from this change.
- Times say nothing here. The same pair took up to 11 times longer in one run than in another, between a
  four-thread run and a one-thread run on a machine with no idle core. The cost of translating each query's
  assertions into its own context could not be told apart from that, and is not measured.

## The pairs the wall-clock backstop ended, listed apart
47 pairs: the backstop ended at least one query of the pair in the runs named. Their time is P2-076's.

| pair | runs that met the backstop |
|---|---|
| `ConEmu.WinForms.ConEmuSession::Init_MakeConEmuCommandLine_EmitConfigFile(System.IO.DirectoryInfo,ConEmu.WinForms.ConEmuStartInfo,ConEmu.WinForms.ConEmuSession.HostContext)` | all four |
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | all four |
| `GitCommands.CustomDiffMergeToolCache::ParseCustomDiffMergeTool(string,string)` | all four |
| `GitCommands.ExecutableExtensions::ExecuteAsync(GitUIPluginInterfaces.IExecutable,GitExtUtils.ArgumentString,System.Action<global::System.IO.StreamWriter>,System.Text.Encoding,GitCommands.CommandCache,bool)` | all four |
| `GitCommands.GitModule::GetFetchArgs(string,string,string,bool?,bool,bool,bool)` | A, B, C |
| `GitCommands.GitModule::GetInteractiveRebasePatchFiles()` | B, C |
| `GitCommands.Patches.Chunk::ParseChunk(string,int,int,int)` | all four |
| `GitCommands.Patches.PatchProcessor::CreatePatchFromString(string[],System.Lazy<global::System.Text.Encoding>,ref int)` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_second_nested_module_changes()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_top_module_changes()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change_commit()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_commit_second_nested_module_change()` | all four |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_no_forced_changes()` | B |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_second_nested_module_change()` | all four |
| `GitExtensions.Plugins.CreateLocalBranches.CreateLocalBranchesForm::button1_Click(object,System.EventArgs)` | all four |
| `GitExtensions.Plugins.FindLargeFiles.FindLargeFilesForm::FindLargeFilesFunction()` | all four |
| `GitExtensions.Plugins.GitImpact.ImpactLoader::LoadModuleInfo(string,GitUIPluginInterfaces.IGitModule,System.Threading.CancellationToken)` | all four |
| `GitExtensions.Plugins.Gource.GourcePlugin::Execute(GitUIPluginInterfaces.GitUIEventArgs)` | A, B, C |
| `GitUI.CommandsDialogs.FormBrowseMenus::CreateToolStripSubMenus(System.Windows.Forms.ToolStrip,System.Windows.Forms.ToolStripMenuItem)` | all four |
| `GitUI.CommandsDialogs.FormClone::OkClick(object,System.EventArgs)` | all four |
| `GitUI.CommandsDialogs.FormClone::OnRuntimeLoad(System.EventArgs)` | all four |
| `GitUI.CommandsDialogs.FormClone::ToTextUpdate(object,System.EventArgs)` | A, B |
| `GitUI.CommandsDialogs.FormDeleteBranch::FormDeleteBranchLoad(object,System.EventArgs)` | A |
| `GitUI.CommandsDialogs.FormFormatPatch::FormatPatch_Click(object,System.EventArgs)` | all four |
| `GitUI.CommandsDialogs.FormResolveConflicts::InitMergetool()` | A, B |
| `GitUI.CommandsDialogs.FormResolveConflicts::SaveAs(string)` | A, B |
| `GitUI.CommandsDialogs.FormVerify.LostObject::TryParse(GitCommands.GitModule,string)` | A |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | A, B |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ShellExtensionSettingsPage::InitializeComponent()` | all four |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.SshSettingsPage::AutoFindPuttyPathsInDir(string)` | A, B |
| `GitUI.Editor.BlameAuthorMargin::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | all four |
| `GitUI.Editor.Diff.DiffViewerLineNumberControl::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | B |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | all four |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessTags(System.Windows.Forms.RichTextBox,System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<int, string>>,bool)` | A, B, C |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::SetXHTMLText(System.Windows.Forms.RichTextBox,string)` | all four |
| `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | all four |
| `GitUI.Script.ScriptOptionsParser::ParseScriptArguments(string,string,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl,GitUIPluginInterfaces.IGitModule,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.GitRevision>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<string>,GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,GitUIPluginInterfaces.GitRevision,string)` | A, B |
| `GitUI.UserControls.EditboxBasedConsoleOutputControl::StartProcess(string,string,string,System.Collections.Generic.Dictionary<string, string>)` | A, B |
| `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::Init(System.Collections.Generic.IReadOnlyList<global::GitUI.UserControls.RevisionGrid.FormQuickItemSelector.ItemData>,string)` | A, B |
| `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::InitializeComponent()` | A, B |
| `GitUI.WindowPositionManager::IsDisplayedOn10Percent(System.Drawing.Rectangle,System.Drawing.Rectangle)` | A, B |
| `GitUIPluginInterfaces.ManagedExtensibility::CreateExportProvider(string)` | A, B |
| `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_add_icon_for_file_extension_only_once()` | A |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` | A, B |
| `ICSharpCode.TextEditor.Document.TextUtilities::GetExpressionBeforeOffset(ICSharpCode.TextEditor.TextArea,int)` | A, B, C |
| `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Setup()` | A, B |

## A first attempt
The first attempt started three harness processes at once, one for A and B and one each for C and D. Each
loaded a different part of the two solutions: 13,168, 13,460 and 7,057 of 13,541 pairs, so 180, 182 and 84
of the 184. Its runs are not the result above. The process that ran A and B on its 180 pairs gives the same
picture: 143 pairs ended by the resource limit or decided in both runs, all 143 identical, and 37 that met
the backstop in one or both.

## Findings
- Every pair the resource limit ends, or that is decided, repeats: 137 of 137 over four runs, on four
  threads and on one, run side by side in one process.
- 47 of 184 pairs met the 60 s backstop on a fully loaded machine, against 8 and 10 on a machine with idle
  cores. A result that ran into the backstop can differ between runs, as VERIFICATION-MODEL.md section 6
  says, and one did. P2-076 owns the time those queries take.
- Several processes loading the same solutions at once each load a different part of them. Not
  investigated; no ticket yet.
