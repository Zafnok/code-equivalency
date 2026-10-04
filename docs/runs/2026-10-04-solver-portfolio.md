# P1-025 solver portfolio spike: gitextensions-8522 (2026-10-04)

Question: of the queries behind the `timeout` Unknowns, how many does a solver other than Z3 decide,
and what are its answers worth?

**Answer: cvc5 answers 59 of the 142 queries Z3 gives up on, within the 60 s Z3 had, and none of
those answers makes a pair Equivalent.** Nine are Divergents that replay, 27 become
Unknown(abstraction), and four `divergence` queries are proved unsatisfiable, which Z3 never did on
a timeout at any budget (`docs/runs/2026-10-01-timeout-budget.md`); on all four the pair still is
not proved, because rung 1's next query, the reachable opaque, is satisfiable (two) or times out in
Z3 (two). Bitwuzla reads none of the files: the call trace is encoded with datatypes, sequences and
integers, which it does not support. Eldarica and Golem were not run: the three large runs hold no
rung 4 Unknown.

**The number of `timeout` Unknowns some other solver proves or refutes: 13 of 164** (four queries
proved unsatisfiable, nine pairs refuted by a replayed model). A further 27 become
Unknown(abstraction).

cvc5 reads the files only with one change to what Z3 prints, described under Method. Without it
cvc5 reads one file of 142.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- SARIF: P2-076's `full` run of that pair (equiv `8e29816`, `runs/20261002-1839-full-after-again`),
  with 608 EQ003 results, 164 of them `unknownReason: timeout`. The three large runs hold 400
  `timeout` Unknowns between them (164, 98 and 138).
- equiv: `main` at `a74f2e0`, win-x64, loaded through the production frontend with the default
  config: bound 3, `resourceLimit` 5,000,000, `timeoutMs` 60,000.
- Tool: `tools/spikes/solver-portfolio/` (about 700 lines). `--self-test` passes: it exports one
  sample's `divergence` query both ways it can answer, and Z3 answers the exported text as it
  answered the in-memory query; the satisfiable one's values, read back as another solver's would
  be, replay to a Divergent.
- Machine: one Windows box, 24 logical cores, six pairs at a time, other work running beside it.
  Loading and lowering took 255 s. Z3's pass over the 164 pairs took 3,374 pair seconds; the two
  solvers and the read-back took 2,969.
- Solvers, each a release binary unpacked under `.corpus/solvers/` and not committed:

| solver | version, as it prints | licence, from its distribution | source | SHA-256 of the archive |
|---|---|---|---|---|
| Bitwuzla | 0.9.1 | MIT (`COPYING`) | github.com/bitwuzla/bitwuzla release `0.9.1`, `Bitwuzla-Win64-x86_64-static.zip` | `d4f623c47409e54254b3f3ce1bd8a7e4a601b19bace5e98113a11bd933ab181f` |
| cvc5 | 1.4.1 (git 2b2e844) | modified BSD (`COPYING`); `--copyright` says the binary links CaDiCaL and SymFPU, and GMP and LibPoly, which are LGPLv3 (`licenses/lgpl-3.0.txt`) | github.com/cvc5/cvc5 release `cvc5-1.4.1`, `cvc5-Win64-x86_64-static.zip` | `1aaafe93484b5ed2cc8a74c2079242e85f160044afea647fadd3e794f7f7e50d` |

  The executables' own SHA-256: `bitwuzla.exe`
  `ce880e5402abc0df542b8c1f9f65e57ba9353690848d3f2d69054ff2f29cc796`, `cvc5.exe`
  `04ee0af071fc18796ce9bf809fceac36e23893f8c2e42c829c4461bc9f0bec91`. Both archive hashes match
  the digests GitHub publishes for the release assets. cvc5 was run with `--arrays-exp`.

## Method
**Which query.** For each `timeout` Unknown the spike builds the pair as `LoopLadder.Bounded` does
(shared fragments made calls, loops unrolled to the bound, the product encoding) and asks rung 1's
queries in order with the production solver and limits: `divergence`, then `opaque`, then `bound`.
The first one Z3 gives up on is the query exported. Rungs 2 and 3 build their obligations
themselves, so a timeout there has no query the spike can reach without a change under `src/`.

**What is written.** The encoding's assertions and the query's terms in a plain solver, as
`Solver.ToString()` prints them, with `set-logic` before, and `check-sat` and a `get-value` over
every Bool and bit-vector constant after. The production query adds its terms with the definitions
substituted in (`Z3Backend.Inline`). That is the same query, since the definitions stay asserted,
and it prints to between 4 MB and 250 MB a file where the plain form is 120 KB to 10 MB, so the
plain form is what is exported.

**The one change.** Z3 prints a `seq.++` of one argument. SMT-LIB's `seq.++` takes two or more and
cvc5 rejects the file. Each solver is run on the file as printed and, when it cannot read that, on
the file with each such `seq.++` written as its argument. The ticket's size guard says to export
what Z3 prints and count what does not parse; both counts are below.

**Read-back (criterion 3).** For a satisfiable answer, the values the solver gave the Bool and
bit-vector constants are asserted beside the query, Z3 completes the model (the functions and the
sorts with no values of their own), and `ModelDecoder.Replay`, the replay rung 1 runs on a model of
its own, decides: Divergent, or Unknown(abstraction). When Z3 cannot complete the model within its
own limits the answer is not counted.

## Export (criterion 1)

| outcome | timeout Unknowns |
|---|---|
| exported: `divergence` query, logic `QF_AUFBVDTSLIA` | 139 |
| exported: `opaque` query, logic `QF_AUFBV` | 3 |
| no query: rung 1 decides the pair at this commit, every query unsatisfiable | 15 |
| no query: rung 1 decides the pair at this commit, `divergence` satisfiable | 4 |
| no query: rung 1 decides the pair at this commit, `opaque` satisfiable | 2 |
| no query: the timeout is on an induction obligation (rungs 2 and 3) | 1 |

142 of 164 export. No file holds one of Z3's own bit-vector operators (`bvsmul_noovfl` and the
like). The 21 pairs rung 1 now decides are no longer timeouts: `main` has moved since the run.

What does not parse elsewhere is in the theories and in the printing:

| construct in the file | files | Bitwuzla | cvc5 |
|---|---|---|---|
| datatypes, sequences and integers (the call trace: `Seq` of an `Event` datatype with an `Int` callee) | 139 | not supported: `unsupported logic` | supported |
| `seq.++` of one argument | the same 139 | n/a | rejected as printed; read once unwrapped |
| a constant array whose default is a constant symbol, not a value | 49 | n/a | rejected: `expected a value` |
| a constant array at all | 9 more | n/a | needs `--arrays-exp` |
| `or` of one argument (the `opaque` queries) | 3 | rejected: `expected at least 2 arguments` | read |

## Answers (criterion 2)
On the 142 exported files, each solver given 60,000 ms of wall-clock time.

| solver | unsatisfiable | satisfiable | unknown | timeout | error | median ms of an answer |
|---|---|---|---|---|---|---|
| Bitwuzla, as printed | 0 | 0 | 0 | 0 | 142 | n/a |
| Bitwuzla, unary `seq.++` unwrapped | 0 | 0 | 0 | 0 | 142 | n/a |
| cvc5, as printed | 0 | 1 | 0 | 0 | 141 | 780 |
| cvc5, unary `seq.++` unwrapped | 4 | 55 | 0 | 34 | 49 | 4,014 |

Without `--arrays-exp` cvc5 answered 4 and 47, with 33 timeouts and 58 errors.

## Satisfiable answers read back (criterion 3)

| query | read back as | results |
|---|---|---|
| `divergence` | Divergent | 9 |
| `divergence` | Unknown(abstraction) | 27 |
| `divergence` | not counted: Z3 cannot complete the model | 18 |
| `opaque` | not counted: Z3 cannot complete the model | 1 |

The 19 not counted are a limit of the spike's read-back, not of cvc5: Z3 is asked the same hard
query again with some constants fixed. A product that decodes cvc5's model itself would not ask it.

## Queries proved unsatisfiable, listed apart (criterion 3)
An unsatisfiable `divergence` query says no input reaching no opaque node makes the sides differ.
The pair is proved only if rung 1's other queries are unsatisfiable too, so Z3 was asked them.

| procedure identity | cvc5 | rung 1's other queries, asked of Z3 |
|---|---|---|
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | 737 ms | pair not proved: `opaque` is satisfiable |
| `GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::Delete_Click(object,System.EventArgs)` | 5,303 ms | pair not proved: `opaque` is satisfiable |
| `GitUI.CommandsDialogs.RevisionFileTreeController::LoadChildren(GitUIPluginInterfaces.IGitItem,System.Windows.Forms.TreeNodeCollection,System.Windows.Forms.ImageList.ImageCollection)` | 24,866 ms | pair not proved: `opaque` is a timeout |
| `GitUI.UserControls.WaitSpinner::.ctor()` | 120 ms | pair not proved: `opaque` is a timeout |

The first two would be Unknown(opaque) in place of Unknown(timeout). On an acyclic pair that is a
`line`-scoped Unknown with the residual claim proved (ADR 0029 decision 4), which a timeout never
is. The other two need the `opaque` query asked of cvc5 as well, which the spike did not do.

## Horn clauses (criterion 4)

| run | chc-timeout | chc-spurious | no-invariant |
|---|---|---|---|
| `gitextensions-8522` `20261002-1839-full-after-again` (608 Unknowns) | 0 | 0 | 0 |
| `gitextensions-9860` `20261003-0055-full` (441 Unknowns) | 0 | 0 | 0 |
| `jellyfin-13023` `20261003-0055-full` (525 Unknowns) | 0 | 0 | 0 |

There are fewer than ten such results, so Eldarica and Golem were not run. Rung 4 does not apply to
a pair that calls, and seldom applies to real code.

## What the answers are worth
- cvc5 moves 38 of 164 `timeout` Unknowns to another answer in the time Z3 had: 9 Divergent, 27
  Unknown(abstraction), 2 Unknown(opaque). That is what P2-050 measured for Z3 at four times the
  budget on its 172 timeouts (37: 14 Divergent, 23 another Unknown), at one times the wall-clock.
- It proves no pair. P2-050 found the same of a larger Z3 budget. A second solver does not change
  what stands between these pairs and a proof.
- The share cvc5 cannot read (49 of 142, the constant arrays) and cannot finish (34) is larger than
  the share it answers. Bitwuzla's share is nothing until the trace has another encoding.
- The trace's theories (sequences of a datatype) are in 139 of 142 hard queries. Whether they are
  what makes the queries hard was not measured here.

## Decisions
- cvc5 decides queries, so criterion 6 asks for the ADR and the ticket: ADR 0050 and P1-033.
- Bitwuzla is not adopted. P1-034 measures a trace encoding in bit-vectors, arrays and functions
  only, which is what would let it read a query.

## Answered results
Procedure identity, the query, and cvc5's answer on the unwrapped file.

| procedure identity | query | cvc5 |
|---|---|---|
| `GitCommands.EnvironmentConfiguration::SetEnvironmentVariables()` | `divergence` | sat: Divergent |
| `GitCommands.GitModule::GetFetchArgs(string,string,string,bool?,bool,bool,bool)` | `divergence` | sat: Divergent |
| `GitCommands.GitModule::HandleConflictSelectSide(string,string)` | `divergence` | sat: Divergent |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ChecklistSettingsPage::ShellExtensionsRegistered_Click(object,System.EventArgs)` | `divergence` | sat: Divergent |
| `GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::Worktrees_CellClick(object,System.Windows.Forms.DataGridViewCellEventArgs)` | `divergence` | sat: Divergent |
| `GitUI.UserControls.RevisionGrid.IndexWatcher::SetFileSystemWatcher()` | `divergence` | sat: Divergent |
| `ICSharpCode.TextEditor.Document.TextUtilities::GetExpressionBeforeOffset(ICSharpCode.TextEditor.TextArea,int)` | `divergence` | sat: Divergent |
| `ICSharpCode.TextEditor.Gui.CompletionWindow.AbstractCompletionWindow::AddShadowToWindow(System.Windows.Forms.CreateParams)` | `divergence` | sat: Divergent |
| `ICSharpCode.TextEditor.Util.TipSection::SetRequiredSize(System.Drawing.SizeF)` | `divergence` | sat: Divergent |
| `AppVeyorIntegration.AppVeyorAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | `divergence` | sat: Unknown(abstraction) |
| `BugReporter.Info.GeneralInfo::.ctor(BugReporter.Serialization.SerializableException)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.AppSettings::LoadEncodings()` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.Git.GitDirectoryResolver::Resolve(string)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.Git.Tag.GitTagController::CreateTag(GitCommands.Git.Commands.GitCreateTagArgs,System.Windows.Forms.IWin32Window)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.GitModule::CommitCmd(bool,bool,string,bool,bool,bool,string,bool)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.GitModule::HandleConflictsSaveSide(string,string,string)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.GitModule::SaveBlobAs(string,string)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.Settings.FileSettingsCache::SaveImpl()` | `divergence` | sat: Unknown(abstraction) |
| `GitExtensions.Program::GetWorkingDir(string[])` | `divergence` | sat: Unknown(abstraction) |
| `GitExtensions.Program::HandleConfigurationException(System.Configuration.ConfigurationException)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.Avatars.GravatarProvider::GetAvatarAsync(string,string,int)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormBrowse::FillUserShells(string)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormFileHistory::saveAsToolStripMenuItem_Click(object,System.EventArgs)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormInit::InitClick(object,System.EventArgs)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormResolveConflicts::TryMergeWithScript(string,string,string,string)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.BuildServerIntegrationSettingsPage::CreateBuildServerSettingsUserControl()` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommitInfo.CommitInfo::.ctor()` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.CommitInfo.CommitInfo::GetSortedTags()` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.Editor.FileViewer::CopyNotStartingWith(char)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessEndElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.FileStatusList::IsFilterMatch(GitCommands.GitItemStatus)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.Infrastructure.Telemetry.DiagnosticsClient::Initialize(bool)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.UserControls.EditboxBasedConsoleOutputControl::StartProcess(string,string,string,System.Collections.Generic.Dictionary<string, string>)` | `divergence` | sat: Unknown(abstraction) |
| `GitUI.UserControls.RevisionGrid.Columns.MessageColumnProvider::DrawCommitMessage(System.Windows.Forms.DataGridViewCellPaintingEventArgs,GitUIPluginInterfaces.GitRevision,GitUI.UserControls.RevisionGrid.CellStyle,System.Drawing.Rectangle,GitUI.UserControls.RevisionGrid.Columns.MultilineIndicator,ref int)` | `divergence` | sat: Unknown(abstraction) |
| `ICSharpCode.TextEditor.TextAreaControl::HandleMouseWheel(System.Windows.Forms.MouseEventArgs)` | `divergence` | sat: Unknown(abstraction) |
| `GitCommands.AppSettings::.cctor()` | `opaque` | sat: Z3 cannot complete the model |
| `GitCommands.PathUtil::FindInFolders(string,System.Collections.Generic.IEnumerable<string>)` | `divergence` | sat: Z3 cannot complete the model |
| `GitCommands.UserRepositoryHistory.RecentRepoSplitter::AddToOrderedMiddleDots(System.Collections.Generic.SortedList<string, global::System.Collections.Generic.List<global::GitCommands.UserRepositoryHistory.RecentRepoInfo>>,GitCommands.UserRepositoryHistory.RecentRepoInfo)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormDeleteBranch::FormDeleteBranchLoad(object,System.EventArgs)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormPull::BranchesDropDown(object,System.EventArgs)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormResolveConflicts::InitMergetool()` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormResolveConflicts::UseMergeWithScript(string,string,string,string,string)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormVerify.LostObject::TryParse(GitCommands.GitModule,string)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.FormAvailableEncodings::LoadEncoding()` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.SshSettingsPage::AutoFindPuttyPathsInDir(string)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | `divergence` | sat: Z3 cannot complete the model |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::SetXHTMLText(System.Windows.Forms.RichTextBox,string)` | `divergence` | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Actions.ToggleLineComment::RemoveCommentAt(ICSharpCode.TextEditor.Document.IDocument,string,ICSharpCode.TextEditor.Document.ISelection,int,int)` | `divergence` | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)` | `divergence` | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` | `divergence` | sat: Z3 cannot complete the model |
| `JenkinsIntegration.JenkinsAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | `divergence` | sat: Z3 cannot complete the model |
| `ResourceManager.CommitDataRenders.CommitDataHeaderRenderer::RenderPlain(GitCommands.CommitData)` | `divergence` | sat: Z3 cannot complete the model |
| `ResourceManager.Translator::GetTranslation(string)` | `divergence` | sat: Z3 cannot complete the model |
| `TranslationApp.Program::Main()` | `divergence` | sat: Z3 cannot complete the model |
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | `divergence` | unsat |
| `GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::Delete_Click(object,System.EventArgs)` | `divergence` | unsat |
| `GitUI.CommandsDialogs.RevisionFileTreeController::LoadChildren(GitUIPluginInterfaces.IGitItem,System.Windows.Forms.TreeNodeCollection,System.Windows.Forms.ImageList.ImageCollection)` | `divergence` | unsat |
| `GitUI.UserControls.WaitSpinner::.ctor()` | `divergence` | unsat |
