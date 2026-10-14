# P1-026 proof-certificate spike: gitextensions-9860 (2026-10-14)

Question: can an Equivalent the solver proves be checked by an independent proof checker, so that
Z3 is no longer trusted for it? cvc5 writes an Alethe proof of each query the Equivalent rests on,
and Carcara checks the proof against the query.

**Answer: yes for 171 of the 302 solver Equivalents of `gitextensions-9860`. Every query of each
of those 171 pairs has a proof Carcara accepts with no hole.** For the whole set the cost is a
median of 170 ms per pair (cvc5 and Carcara together), and the largest is 35 s. Carcara rejected
no proof. Another 25 pairs are accepted only with holes. Each hole is a bit-blasting step of
cvc5's that Carcara does not check. Twelve stop at a rewrite rule missing from the rule file.
The other 94 have no proof to check: cvc5 cannot read the query (76), Alethe cannot express the
proof (12 lockstep pairs, whose call traces are sequences), cvc5 runs out of time (4), or the
proof is a later round of rung 1 that the spike does not rebuild (2).

What the 171 have in common: none of their proofs holds a single bit-blasting step. Each closes
by congruence and rewriting: the two sides compute the same terms. A query that needs reasoning
about bits is today either holey or stopped. On this tool's queries, the cvc5-to-Carcara chain
certifies the "same computation, written differently" proofs, and not yet the arithmetic ones.

## The limit
A certificate covers the query and nothing upstream of it. It says that the SMT-LIB text handed to
cvc5 is unsatisfiable. It does not say that the lowering turned each C# body into IR that means what
the code means, nor that the encoder turned the IR into formulas that mean what the IR means.
`ProductEncoder`, `IrUnroller`, `IrFragmenter`, the frontend and ADR 0050's printer all stay
trusted. A wrong lowering produces a query that is honestly unsatisfiable and a certificate that
honestly checks.

An Equivalent by congruence has no query at all. Both bodies lower to the same IR, and no solver
is asked anything. That is 95.4% of matched pairs over the five large runs (55,784 of 58,477;
README scoreboard), and 98.8% of their Equivalents. On the three runs the ticket counted, it was
94.6%. A certificate says nothing about those pairs either. M0-012's differential gate is what
covers the lowering, the encoder and congruence: it checks that the IR runs as the compiled code
runs.

So a certificate removes exactly one thing from what has to be trusted: Z3's (or cvc5's) answer
on the queries of a solver Equivalent. P2-059 showed that answer can be wrong.

## Setup
- Pair: human, gitextensions/gitextensions PR #9860 (net5.0 to net6.0), legacy bcd0c2617bdd,
  modern 37797ea4dd74 (`tools/corpus/pairs.csv`).
- SARIF: P2-124's thorough run of that pair (`runs/20261012-2040-full-thorough`, equiv
  `1d767f54`). It has 302 EQ001 results with `proofMethod` `bounded` (285) or
  `lockstep-induction` (17). Deviation: the ticket counted 239 on the 2026-10-03 run. The spike
  rebuilds every pair with today's frontend, so it takes the latest thorough run of the pair. The
  older run's identities would describe an older lowering.
- equiv: `main` at `e98fd1b9` (merged up to `31cc1c09`, which changes nothing under `src/`), win-x64,
  default config: bound 3, `resourceLimit` 2,000,000, `timeoutMs` 60,000. The solutions load and
  lower through the production frontend in 147 s (14,073 matched pairs).
- Tool: `tools/spikes/proof-certificates/` (about 700 lines). `--self-test` passes: the sample pair
  `a + b` against `b + a` exports two queries, cvc5 proves both, and Carcara accepts both with no
  hole.
- Machine: one Windows box with 24 logical cores, six results at a time. Another session's cvc5
  spike ran six cvc5 processes alongside the whole time. Certifying the 302 results took 166 s of
  wall clock and 774 result-seconds, which includes Z3 asking each query again.
- Tools, none committed, all under the git-ignored `.cvc5/` and `.corpus/carcara/`:

| tool | version | licence, from its distribution | source | SHA-256 |
|---|---|---|---|---|
| cvc5 | 1.4.1 (git 2b2e844) | modified BSD; the binary links GMP and LibPoly (LGPL-3.0) | `tools/cvc5/fetch.ps1`, release `cvc5-1.4.1`, `cvc5-Win64-x86_64-static.zip` | archive `1aaafe93484b5ed2cc8a74c2079242e85f160044afea647fadd3e794f7f7e50d` (the pin), `cvc5.exe` `04ee0af071fc18796ce9bf809fceac36e23893f8c2e42c829c4461bc9f0bec91` |
| Carcara | commit `20bf49c5ce31089ac83118574c888669355aaae1` (2026-10-13; `Cargo.toml` says 1.1.0) | Apache-2.0 (`LICENSE`) | github.com/ufmg-smite/carcara source tarball of that commit, built with Rust 1.98.1 (x86_64-pc-windows-gnu) | tarball `c04f74cc485ffd61cadcd02a094fdc3573f0e2177191f16db63afb4f8583327b`, `carcara.exe` `f57147b81c0113c2b9d8811ec3d1324c34eaa488757858a6b97a550c3d62b549` |
| GMP, for Carcara | 6.3.0 (`mingw-w64-ucrt-x86_64-gmp` 6.3.0-2, MSYS2) | LGPL-3.0, linked dynamically | the box's MSYS2 install | `libgmp-10.dll` `c2567ccf3f410c3411ba7cd4b7ba14f393a0e70a1d0c0593297df6706b5fba96` |
| RARE rule file | `proofs/alethe/rewrites.eo`, 119 rules | BSD-3-Clause (cvc5) | github.com/cvc5/cvc5 at `f0bc3373ef032a3fc5006f22778aaae7b274f429` | `24c5083754312842b951b1af2a59c2c29f571562b94d7fc8c04c5469e87b82ea` |

  Carcara has no Windows release. Its newest release, 1.1.0 from 2023, predates cvc5 1.4.1's
  Alethe output. The commit used is the one cvc5's own CI pins (`contrib/get-carcara-checker`).
  The README of `tools/spikes/proof-certificates/` describes the Windows build. Carcara is a
  small academic project, about 47 GitHub stars, from one research group. The ADR treats that as
  a risk.

## Method
**Which queries.** For each result the spike rebuilds the proof as `LoopLadder.Climb` does. First
the pair, with its shared fragments made calls. A `bounded` result gets rung 1's `divergence` and
`opaque` queries, plus `bound` when the pair loops, on the unrolled product, with call traces
compared by position (P1-038). A `lockstep-induction` result gets rung 2's base obligation and one
step obligation per paired loop, on the cut procedures, with traces compared as sequences. Each
query is asked again of Z3 with the production solver and limits. A query Z3 does not answer
unsatisfiable means the proof is not rebuilt at this commit, and the result is counted apart: that
happened twice, both proofs from a later round of rung 1 (abstraction refinement), which the spike
does not rebuild.

**What is written.** `SecondSolver.Print`, the printer P1-025 measured and ADR 0050 made the
product's, with `produce-proofs` in place of `produce-models` and no `get-value`. Where it
declines a constant array whose default is not a value, the spike writes the query as Z3 prints
it, as P1-025 did. cvc5 rejects every such file, with or without `--arrays-exp`: the default is
an uninterpreted literal, and cvc5 wants a value there.

**How it is proved and checked.** `cvc5 --dump-proofs --proof-format-mode=alethe
--proof-granularity=dsl-rewrite --arrays-exp --tlimit=60000`, then `carcara check
--allow-int-real-subtyping --expand-let-bindings --ignore-unknown-rules --rare-file rewrites.eo
<proof> <query>`, with a 300 s wall clock. These are the flags cvc5's regression tests check
Alethe proofs with (`run_regression.py`, `AletheTester`), with two left out:
- `--proof-alethe-testing`, which makes cvc5 refuse every proof holding `bvadd`, and so every
  query here;
- `--allowed-rules`, which tells Carcara to count unchecked rules as checked.

A hole is a step Carcara does not check: a `hole` step, a `lia_generic` step, or a rule it does not
know (`--ignore-unknown-rules`; the known rules are `get_rule` at that commit). When Carcara
reaches a `rare_rewrite` step whose rule is not in the rule file, it stops and prints `invalid`.
The spike counts that apart, as "stopped". The rule there is missing, not shown wrong, and the
rest of the proof goes unchecked.

## Per result
| proof method | outcome | results |
|---|---|---|
| bounded | **certified: every query accepted, no hole** | **171** |
| bounded | cvc5 cannot read a query (a constant array the printer does not rewrite) | 71 |
| bounded | accepted with holes | 25 |
| bounded | Carcara stops at a RARE rule the rule file lacks | 12 |
| bounded | no Alethe proof: cvc5 ran out of time writing it | 3 |
| bounded | proof not rebuilt: a later round of rung 1 | 2 |
| bounded | cvc5 times out on a query | 1 |
| lockstep-induction | no Alethe proof: cvc5 says `seq.unit` is unsupported by Alethe | 12 |
| lockstep-induction | cvc5 cannot read a query (a constant array the printer does not rewrite) | 5 |
| **all** | | **302** |

No `lockstep-induction` result is certified. Rung 2 compares call traces as sequences, and cvc5
cannot write a sequence proof in Alethe. Each of the 171 certified results has two queries,
`divergence` and `opaque`, and none of them loops.

## Per query
| proof method | query | printed | outcome | queries |
|---|---|---|---|---|
| bounded | opaque | `SecondSolver.Print` | Carcara valid | 212 |
| bounded | divergence | `SecondSolver.Print` | Carcara valid | 171 |
| bounded | divergence | `SecondSolver.Print` | Carcara holey | 25 |
| bounded | divergence | `SecondSolver.Print` | Carcara stopped: a RARE rule the rule file lacks | 12 |
| bounded | divergence | `SecondSolver.Print` | unsat, no Alethe proof (out of time writing it) | 3 |
| bounded | divergence | `SecondSolver.Print` | cvc5 timeout | 1 |
| bounded | divergence, opaque, bound | as Z3 prints it | cvc5 cannot read it | 143 |
| lockstep-induction | base, step | `SecondSolver.Print` | unsat, no Alethe proof (`seq.unit`) | 35 |
| lockstep-induction | base, step | `SecondSolver.Print` | unsat, no Alethe proof (out of time writing it) | 2 |
| lockstep-induction | base, step | as Z3 prints it | cvc5 cannot read it | 6 |

Totals over the 610 unsatisfiable queries:

| outcome | queries |
|---|---|
| cvc5 unsatisfiable | 460 |
| an Alethe proof written | 420 |
| Carcara accepts, no hole | 383 |
| Carcara accepts with holes | 25 |
| Carcara stops at a missing RARE rule | 12 |
| Carcara rejects | **0** |
| cvc5 cannot read the file | 149 |
| cvc5 timeout | 1 |

## Cost
| measure | over | queries | median | largest |
|---|---|---|---|---|
| cvc5 time, unsat with the proof written out | every unsat answer | 460 | 52 ms | 64,085 ms |
| Carcara time | every proof checked | 420 | 22 ms | 12,595 ms |
| proof size | every proof written | 420 | 80,193 bytes | 626,577,120 bytes |
| cvc5 time | queries accepted with no hole | 383 | 42 ms | 27,917 ms |
| Carcara time | queries accepted with no hole | 383 | 20 ms | 8,054 ms |
| proof size | queries accepted with no hole | 383 | 66,856 bytes | 65,151,337 bytes |
| cvc5 and Carcara together, per certified pair | the 171 pairs | 171 | 170 ms | 34,671 ms |

The largest proof, 627 MB, is a `divergence` proof Carcara stops in at a missing RARE rule after
6.9 s. Production would have to bound proof size as well as time.

## Holes and stops
| in proofs Carcara reports holey (25) | proofs |
|---|---|
| unchecked rule `bv_bitblast_step_const` | 25 |
| unchecked rule `bv_bitblast_step_bvequal` | 25 |
| unchecked rule `bv_bitblast_step_var` | 25 |
| unchecked rule `bv_bitblast_step_bvadd` | 14 |

| RARE rule the file lacks, where Carcara stops (12) | proofs |
|---|---|
| `bv-sge-eliminate` | 6 |
| `bv-sgt-eliminate` | 4 |
| `bv-ult-zero-1` | 2 |

Both are bit-vector gaps in the chain, not version skew. cvc5's `main` still emits the
`bv_bitblast_step_*` rules (`src/proof/alethe/alethe_proof_rule.h`). Carcara at the pinned commit
checks a different set of bit-blasting rules (`bitblast_*`). And `rewrites.eo` holds only the 119
rules cvc5's CI needs. cvc5's CI runs with `--proof-alethe-testing`, so no bit-vector proof is ever
checked there.

## Findings, each with its owner
- The certified pairs are the "same terms" proofs. No certified proof contains a bit-blasting
  step, and every proof that needs one is holey or stopped. ADR 0057 records what a pair with holes
  reports.
- 76 results (149 queries) cannot be sent to cvc5. Their constant arrays have a default that is no
  value, and `SecondSolver.Print` declines them. This is also why they never reach cvc5 as the
  second solver. Ticket P2-179.
- No rung 2 proof can be written in Alethe, because its obligations compare call traces as
  sequences (`seq.unit`). Rung 1 has compared them by position since P1-038. Ticket P1-042.
- 4 queries reach unsat but cvc5 runs out of its 60 s while writing the proof, and one proof is
  627 MB. The build ticket P1-041 sets budgets for time and proof size.
- 2 proofs from a later round of rung 1 (abstraction refinement) are outside the spike. P1-041
  covers every round.

## Certified pairs
All 171 are `bounded`, each with its `divergence` and `opaque` queries accepted with no hole:

- `AppVeyorIntegration.AppVeyorAdapter::UpdateDescription(AppVeyorIntegration.AppVeyorBuildInfo,System.Threading.CancellationToken)`
- `AzureDevOpsIntegration.ApiClient::GetBuildDefinitionNameFromIdAsync(int)`
- `AzureDevOpsIntegration.ApiClient::InitializeHttpClient(string,string)`
- `AzureDevOpsIntegration.ApiClient::QueryForBuildStatus(string,string)`
- `AzureDevOpsIntegration.AzureDevOpsAdapter::get_UniqueKey()`
- `CommonTestUtils.MEF.ExportProviderCache.SingleExportProviderFactory::RequireForSingleExportProvider(bool)`
- `ConEmu.WinForms.ConEmuSession::.ctor(ConEmu.WinForms.ConEmuStartInfo,ConEmu.WinForms.ConEmuSession.HostContext,Microsoft.VisualStudio.Threading.JoinableTaskFactory)`
- `ConEmu.WinForms.GuiMacroExecutor::LoadConEmuDll(string)`
- `GitCommands.AppSettings::SetDocumentationBaseUrl(string)`
- `GitCommands.AppSettings::get_DocumentationBaseUrl()`
- `GitCommands.AppTitleGenerator::Initialise(string,string)`
- `GitCommands.CommitDataManager::CreateFromRevision(GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.ObjectId>)`
- `GitCommands.CommitDataManager::GetModule()`
- `GitCommands.CommitDataManager::UpdateBody(GitCommands.CommitData,bool,out string)`
- `GitCommands.CommitTemplateManager::GetModule()`
- `GitCommands.Config.ConfigFile.ConfigFileParser::NewKey()`
- `GitCommands.Config.ConfigFile.ConfigFileParser::NewValue()`
- `GitCommands.Git.AheadBehindDataProvider::GetGitExecutable()`
- `GitCommands.Git.Commands.GitCheckoutBranchCmd::BuildArguments()`
- `GitCommands.Git.Commands.GitCommandHelpers::AddSubmoduleCmd(string,string,string,bool)`
- `GitCommands.Git.Commands.GitCommandHelpers::GetAllChangedFilesCmd(bool,GitCommands.UntrackedFilesMode,GitCommands.IgnoreSubmodulesMode,bool)`
- `GitCommands.Git.Commands.GitCommandHelpers::PushTagCmd(string,string,bool,GitCommands.Git.ForcePushOptions)`
- `GitCommands.Git.GetAllChangedFilesOutputParser::GetModule()`
- `GitCommands.Git.GitStash::get_Name()`
- `GitCommands.GitModule::ExistsMergeCommit(string,string)`
- `GitCommands.GitModule::GetAllTagsWhichContainGivenCommit(GitUIPluginInterfaces.ObjectId)`
- `GitCommands.GitModule::GetCommitCount(string,string,bool,bool)`
- `GitCommands.GitModule::GetFileContentsAsync(string)`
- `GitCommands.GitModule::GetFullTree(string)`
- `GitCommands.GitModule::GetIgnoredFiles(System.Collections.Generic.IEnumerable<string>)`
- `GitCommands.GitModule::GetParents(GitUIPluginInterfaces.ObjectId)`
- `GitCommands.GitModule::GetPuttyKeyFileForRemote(string)`
- `GitCommands.GitModule::GetRemoteBranch(string)`
- `GitCommands.GitModule::GetStashDiffFiles(string)`
- `GitCommands.GitModule::HandleConflictSelectSide(string,string)`
- `GitCommands.GitModule::OpenFilesWithDifftool(string,string,string)`
- `GitCommands.GitModule::OpenWithDifftool(string,string,string,string,string,bool,string)`
- `GitCommands.GitModule::PushCmd(string,string,string,GitCommands.Git.ForcePushOptions,bool,int)`
- `GitCommands.GitModule::RunGitK()`
- `GitCommands.GitModule::RunMergeTool(string,string)`
- `GitCommands.GitModule::ShowObject(GitUIPluginInterfaces.ObjectId)`
- `GitCommands.GitModule::TryResolvePartialCommitId(string,out GitUIPluginInterfaces.ObjectId)`
- `GitCommands.GitModule::UpdateIndex(System.IO.StreamWriter,string)`
- `GitCommands.GitPushAction::ToString()`
- `GitCommands.GitRef::get_LocalName()`
- `GitCommands.Gpg.GitGpgController::GetModule()`
- `GitCommands.PathUtil::GetDisplayPath(string)`
- `GitCommands.Plink::ConnectAsync(string)`
- `GitCommands.Remotes.ConfigFileRemoteSettingsManager::GetModule()`
- `GitCommands.Remotes.ConfigFileRemoteSettingsManager::RemoveRemote(GitCommands.Remotes.ConfigFileRemote)`
- `GitCommands.Settings.BuildServerSettings::get_IntegrationEnabled()`
- `GitCommands.Settings.BuildServerSettings::get_ServerName()`
- `GitCommands.Settings.BuildServerSettings::get_SettingsSource()`
- `GitCommands.Settings.BuildServerSettings::get_ShowBuildResultPage()`
- `GitCommands.Settings.BuildServerSettings::set_IntegrationEnabled(bool?)`
- `GitCommands.Settings.BuildServerSettings::set_ServerName(string)`
- `GitCommands.Settings.BuildServerSettings::set_ShowBuildResultPage(bool?)`
- `GitCommands.Settings.DetailedSettings::get_AddMergeLogMessages()`
- `GitCommands.Settings.DetailedSettings::get_GetRemoteBranchesDirectlyFromRemote()`
- `GitCommands.Settings.DetailedSettings::get_MergeLogMessagesCount()`
- `GitCommands.Settings.DetailedSettings::set_AddMergeLogMessages(bool)`
- `GitCommands.Settings.DetailedSettings::set_GetRemoteBranchesDirectlyFromRemote(bool)`
- `GitCommands.Settings.DetailedSettings::set_MergeLogMessagesCount(int)`
- `GitCommands.Settings.SettingsPath::PathFor(string)`
- `GitCommands.Submodules.SubmoduleStatusProvider::GetModuleBranch(string,string)`
- `GitCommandsTests.Config.ConfigFileTest::GetConfigValue(string,string)`
- `GitCommandsTests.ExternalLinks.ExternalLinksManagerIntegrationTests::Setup()`
- `GitCommandsTests.Git.Commands.GitCommandsHelperTest::TestMergedBranchesCmd(bool,bool,string)`
- `GitCommandsTests.Git.ExecutableExtensionsTests::GetOutput_with_cache_hit()`
- `GitCommandsTests.Git.GitRefTests::If_Remote_is_not_prefix_of_Name_then_LocalName_should_return_Name()`
- `GitCommandsTests.Git.GitRefTests::Remote_Should_prefix_LocalName_for_Name()`
- `GitCommandsTests.GitModuleTests::AddRemote()`
- `GitCommandsTests.GitModuleTests::RemoveRemote()`
- `GitCommandsTests.GitModuleTests::RenameBranch()`
- `GitCommandsTests.GitModuleTests::RenameRemote()`
- `GitCommandsTests.GitRevisionSummaryBuilderTests::Should_do_ellipsis(string)`
- `GitCommandsTests.Settings.BuildServerSettingsTests::Setup()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_bool_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_byte_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_char_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_enum_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_float_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_int_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_nullable_struct_setting()`
- `GitCommandsTests.Settings.SettingTests::Should_create_setting`1(T)`
- `GitCommandsTests.Settings.SettingTests::Should_create_string_setting(string)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_bool_setting(bool?)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_byte_setting(byte?)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_char_setting(char?)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_enum_setting(GitCommandsTests.Settings.SettingTests.TestEnum?)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_float_setting(float?)`
- `GitCommandsTests.Settings.SettingTests::Should_save_nullable_int_setting(int?)`
- `GitExtUtils.GitArgumentBuilder::.ctor(string,GitExtUtils.GitCommandConfiguration,GitExtUtils.ArgumentString)`
- `GitExtensions.Plugins.Bitbucket.PullRequest::get_DestDisplayName()`
- `GitExtensions.Plugins.Bitbucket.PullRequest::get_SrcDisplayName()`
- `GitExtensions.Plugins.GitHub3.GitHub3Plugin::GenerateTokenLink_Click(object,System.EventArgs)`
- `GitExtensions.Plugins.GitHub3.GitHub3Plugin::ManageTokenLink_Click(object,System.EventArgs)`
- `GitExtensions.Plugins.GitHub3.GitHub3Plugin::get_GitHubApiEndpoint()`
- `GitExtensions.Plugins.GitHub3.GitHub3Plugin::get_GitHubEndpoint()`
- `GitExtensions.Plugins.GitHub3.GitHubHostedRemote::GetBlameUrl(string,string,int)`
- `GitExtensions.Plugins.GitHub3.GitHubRepo::ToString()`
- `GitExtensions.Plugins.GitImpact.ImpactLoader::GetTasks(System.Threading.CancellationToken)`
- `GitExtensions.Plugins.JiraCommitHintPlugin.JiraCommitHintPlugin::.ctor()`
- `GitUI.AutoCompletion.CommitAutoCompleteProvider::GetModule()`
- `GitUI.Avatars.CustomAvatarProvider.UriTemplateResolver::GetAvatarAsync(string,string,int)`
- `GitUI.BranchTreePanel.RepoObjectsTree.BaseBranchNode::DisplayText()`
- `GitUI.CommandsDialogs.BuildReportTabPageExtension::GetModule()`
- `GitUI.CommandsDialogs.CommitDialog.FormCommitTemplateSettings::RefreshLineInListBox(int)`
- `GitUI.CommandsDialogs.FormBlame::FormBlameLoad(object,System.EventArgs)`
- `GitUI.CommandsDialogs.FormBrowse::RevisionInfo_CommandClicked(object,ResourceManager.CommandEventArgs)`
- `GitUI.CommandsDialogs.FormCleanupRepository::GetCleanMode()`
- `GitUI.CommandsDialogs.FormEditor::FormEditor_FormClosing(object,System.Windows.Forms.FormClosingEventArgs)`
- `GitUI.CommandsDialogs.FormEditor::SaveChangesShowException()`
- `GitUI.CommandsDialogs.FormRemotes::application_Idle(object,System.EventArgs)`
- `GitUI.CommandsDialogs.FormVerify::SaveObjectsClick(object,System.EventArgs)`
- `GitUI.CommandsDialogs.RepoHosting.ForkAndCloneForm::Init()`
- `GitUI.CommandsDialogs.RevisionDiffControl::DescribeRevision(GitUIPluginInterfaces.ObjectId,int)`
- `GitUI.CommandsDialogs.RevisionDiffControl::RefreshArtificial()`
- `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::.ctor()`
- `GitUI.CommandsDialogs.SettingsDialog.Pages.EditorHelper::GetEditorCommandLine(string,string,string,string[])`
- `GitUI.CommandsDialogs.SettingsDialog.Pages.EditorHelper::get_FileEditorCommand()`
- `GitUI.CommandsDialogs.SettingsDialog.RevisionLinks.AzureDevopsExternalLinkDefinitionExtractor::GetDefinitions(string)`
- `GitUI.CommandsDialogs.SettingsDialog.RevisionLinks.GitHubExternalLinkDefinitionExtractor::GetDefinitions(string)`
- `GitUI.CommandsDialogs.WorktreeDialog.FormCreateWorktree::CreateWorktree()`
- `GitUI.CommitInfo.CommitInfo::copyCommitInfoToolStripMenuItem_Click(object,System.EventArgs)`
- `GitUI.Editor.FileViewer::GetExtraDiffArguments(bool)`
- `GitUI.FileStatusDiffCalculator::GetModule()`
- `GitUI.GitModuleControl::set_UICommandsSource(GitUI.IGitUICommandsSource)`
- `GitUI.GitModuleForm::get_UICommands()`
- `GitUI.GitUICommands::StartCreateBranchDialog(System.Windows.Forms.IWin32Window,string)`
- `GitUI.GitUICommands::StartResetCurrentBranchDialog(System.Windows.Forms.IWin32Window,string)`
- `GitUI.Infrastructure.Telemetry.FormBrowseDiagnosticsReporter::Report()`
- `GitUI.MessageBoxes::FailedToRunShell(System.Windows.Forms.IWin32Window,string,System.Exception)`
- `GitUI.OsShellUtil::SelectPathInFileExplorer(string)`
- `GitUI.RepositoryCurrentBranchNameProvider::GetCurrentBranchName(string)`
- `GitUI.Shells.BashShell::GetChangeDirCommand(string)`
- `GitUI.Shells.CmdShell::GetChangeDirCommand(string)`
- `GitUI.Shells.ConEmuControlExtensions::ChangeFolder(ConEmu.WinForms.ConEmuControl,GitUI.Shells.IShellDescriptor,string)`
- `GitUI.Shells.PowerShellShell::GetChangeDirCommand(string)`
- `GitUI.Shells.PwshShell::GetChangeDirCommand(string)`
- `GitUI.SortableBindingList`1::CreateComparison(System.ComponentModel.PropertyDescriptor,bool)`
- `GitUI.Theming.ThemeException::.ctor(string,string,System.Exception)`
- `GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string)`
- `GitUI.UserControls.FilterToolBar::Bind(System.Func<global::GitUIPluginInterfaces.IGitModule>,GitUI.UserControls.RevisionGrid.IRevisionGridFilter)`
- `GitUI.UserControls.FilterToolBar::GetModule()`
- `GitUI.UserControls.FilterToolBar::get_RevisionGridFilter()`
- `GitUI.UserControls.RevisionGrid.Columns.AuthorNameColumnProvider::GetAuthorAndCommiterToolTip(GitUIPluginInterfaces.GitRevision)`
- `GitUIPluginInterfaces.BuildServerIntegration.BuildDurationFormatter::Format(long?)`
- `GitUIPluginInterfaces.CredentialsManager.AdysTechCredentialManagerWrapper::GetTarget(string)`
- `GitUIPluginInterfaces.CredentialsManager::GetWindowsCredentialsTarget(string,GitUIPluginInterfaces.SettingLevel)`
- `GitUIPluginInterfaces.GitRevision::ToString()`
- `GitUIPluginInterfaces.ObjectId::Parse(string)`
- `GitUIPluginInterfaces.ObjectId::Parse(string,System.Text.RegularExpressions.Capture)`
- `GitUIPluginInterfaces.ObjectId::Parse(string,int)`
- `GitUIPluginInterfaces.RepositoryHosts.GitProtocolExtensions::IsUrlUsingHttp(string)`
- `GitUITests.GitUICommandsTests.RunCommandTests::RunCommandBasedOnArgument_history(string,bool,bool)`
- `GitUITests.UserControls.CommitInfo.RefsFormatterTests::FormatRef(string,string,bool)`
- `GitUITests.UserControls.CommitInfo.RefsFormatterTests::GetShowAllLink(string)`
- `GitUITests.UserControls.TreeViewExtensionsTests::RestoreExpandedNodesState_should_restore_all_nodes()`
- `ICSharpCode.TextEditor.Document.ResourceSyntaxModeProvider::GetSyntaxModeFile(ICSharpCode.TextEditor.Document.SyntaxMode)`
- `JenkinsIntegration.JenkinsAdapter::CreateBasicHeader(string,string)`
- `Microsoft.Build.Utilities.CommandLineBuilder::VerifyThrowNoEmbeddedDoubleQuotes(string,string)`
- `ResourceManager.GitExtensionsControlInitialiser::InitializeComplete()`
- `ResourceManager.GitExtensionsControlInitialiser::LoadHandler(object,System.EventArgs)`
- `ResourceManager.LinkFactory::CreateBranchLink(string)`
- `ResourceManager.LinkFactory::CreateShowAllLink(string)`
- `ResourceManager.LinkFactory::CreateTagLink(string)`
- `ResourceManager.LinkFactory::ExecuteLink(string,System.Action<global::ResourceManager.CommandEventArgs>,System.Action<string>)`
- `ResourceManager.Xliff.TranslationBody::AddTranslationItem(ResourceManager.Xliff.TranslationItem)`
- `ResourceManager.Xliff.TranslationBody::AddTranslationItemIfNotExist(ResourceManager.Xliff.TranslationItem)`
- `System.StringExtensions::Quote(string,string)`
