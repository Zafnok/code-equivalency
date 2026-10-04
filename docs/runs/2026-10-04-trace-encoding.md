# P1-034 trace encoding spike: gitextensions-8522 (2026-10-04)

Question: do the rung 1 queries Z3 gives up on get easier when "the call traces are equal" is
written by position, in bit-vectors and functions, and not as an equality of two sequences of a
datatype?

**Answer: yes. Z3, which answers none of the 142 queries in the sequence encoding, answers 72 of
them in the positional one, at the same resource limit, in a median of half a second. No pair
becomes Equivalent.** 26 of the 72 replay to a Divergent, 42 to Unknown(abstraction), and four
`divergence` queries are proved unsatisfiable; on all four the pair is still not proved, because
rung 1's next query, the reachable opaque, is satisfiable.

**The number of the 142 the positional encoding decides that the sequence encoding does not, per
solver: Z3 72 (4 of them proofs of the query), cvc5 24 (none a proof; it loses 2 it decided in the
sequence form), Bitwuzla 103 (3 proofs) once each uninterpreted sort is defined as a bit-vector, and
0 as the file is written. None of the proofs proves a pair.**

Some solver decides 124 of the 142 in the positional encoding, against 56 in the sequence one. No
query has a satisfiable answer from one run and an unsatisfiable one from another.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- Queries: the 142 rung 1 queries P1-025 exported (`docs/runs/2026-10-04-solver-portfolio.md`), 139
  `divergence` and 3 `opaque`, taken by identity from that run's `results.tsv`.
- equiv: `main` at `ab66987`, win-x64, Z3 5.1.0, loaded through the production frontend with the
  default config: bound 3, `resourceLimit` 5,000,000, `timeoutMs` 60,000.
- Tool: `tools/spikes/trace-encoding/` (about 800 lines; the export, solver runs and read-back are
  P1-025's, compiled in). Its README gives the positional form and why it says what the sequence
  form says on an unrolled pair. `--self-test` passes: nine hand-written pairs whose only observable
  is the trace, each answered the same by Z3 in both encodings and as expected.
- On every one of the 142, the spike's rebuilt query, given production's own trace and
  exception-type conjuncts, is the very term `ProductEncoder.Encode` returned (the tool throws
  otherwise). So the two encodings differ in those two conjuncts and nothing else.
- Solvers: Bitwuzla 0.9.1 and cvc5 1.4.1 (with `--arrays-exp`), the release binaries P1-025 names
  with their hashes, not committed.
- Machine: one Windows box, 24 logical cores, six queries at a time. Loading and lowering took
  218 s; the 142 queries took 11,211 query seconds over 40 minutes.

## Method
**The positional form.** A call site's position is the encoding's own `cnt` term. The traces are
equal when they are as long and any two call sites, one a side, that are both reached at the same
position are the same event: the same canonical callee and argument types, and equal arguments and
heap read. Pairs of sites whose positions cannot meet on any path are left out. The exception type,
an integer in production, is compared as a bit-vector. The assertions are production's own.

**What is asked.** Z3 is asked each query in memory in both encodings, with the production solver,
tactic pipeline and limits (`Z3Backend.Query`, `Check`). The `opaque` queries do not compare the
traces, so they are one query in both encodings and Z3 is asked once. cvc5 and Bitwuzla get the
positional query as a file, as Z3 prints it from a plain solver with each `seq.++`, `or` and `and`
of one argument written as that argument, for 60,000 ms. cvc5 is also run on the sequence file in
the same pass, as the control: P1-025's numbers are from another commit and another load.

**Bitwuzla and uninterpreted sorts.** Bitwuzla 0.9.1 answers `unknown` to an equality over an
uninterpreted sort and crashes on an array of one. The product has both whatever the trace is: every
reference type is a sort. Bitwuzla is therefore also run on the positional file with each
`declare-sort` written as a `define-sort` of a 64-bit vector. That keeps the answer (a model over
bit-vectors is a model of the sorts, and a quantifier-free query with a model has one with fewer
elements of a sort than it has terms of it) and changes one line of text per sort. Both rows are
reported.

**Read-back.** As P1-025: a satisfiable answer from another solver has its Bool and bit-vector
values asserted beside the query, Z3 completes the model, and `ModelDecoder.Replay` decides. Z3's
own satisfiable answers are replayed from its model, as rung 1 does.

## Products (criterion 1)

| query | logic, sequence | logic, positional | queries |
|---|---|---|---|
| `divergence` | QF_AUFBVDTSLIA | QF_AUFBV | 139 |
| `opaque` | QF_AUFBV | QF_AUFBV | 3 |

All 142 build. Over the 139 `divergence` products a side has a median of 36 call sites and at most
612; the positional form compares a median of 141 pairs of sites and at most 325,266.

## Answers (criterion 3)
Z3 at `resourceLimit` 5,000,000; cvc5 and Bitwuzla 60,000 ms of wall-clock time.

| solver, encoding | unsatisfiable | satisfiable | unknown | timeout | error | median ms of an answer |
|---|---|---|---|---|---|---|
| Z3, sequence | 0 | 0 | 116 | 26 | 0 | n/a |
| Z3, positional | 4 | 68 | 70 | 0 | 0 | 489 |
| cvc5, sequence (control) | 4 | 52 | 0 | 37 | 49 | 8,411 |
| cvc5, positional | 3 | 75 | 0 | 15 | 49 | 1,509 |
| Bitwuzla, positional, as written | 0 | 0 | 46 | 2 | 94 | n/a |
| Bitwuzla, positional, sorts as 64-bit vectors | 3 | 100 | 30 | 9 | 0 | 605 |

Z3's `unknown` is the resource limit, its `timeout` the wall-clock backstop. The slowest answer Z3
gives in the positional form takes 5,042 ms. cvc5's 49 errors are the constant arrays whose default
is not a value, in both encodings, as P1-025 found; P1-033 handles them. Bitwuzla's 94 errors as
written are one crash (`std::bad_variant_access`), and its 46 `unknown` come within a second.

| solver, positional | decides, sequence does not | of those, the query proved | sequence decides, positional does not | both decide | answers disagree |
|---|---|---|---|---|---|
| Z3 | 72 | 4 | 0 | 0 | 0 |
| cvc5 | 24 | 0 | 2 | 54 | 0 |
| Bitwuzla, as written (it reads no sequence file) | 0 | 0 | n/a | n/a | n/a |
| Bitwuzla, sorts as 64-bit vectors (it reads no sequence file) | 103 | 3 | n/a | n/a | n/a |

Between solvers, in the positional form: Bitwuzla decides 46 that Z3 does not, Z3 15 that Bitwuzla
does not, and cvc5 6 that neither does. All three of the `opaque` queries are satisfiable for
Bitwuzla; Z3 answers none and cvc5 one.

By the number of pairs of sites the `divergence` query compares:

| pairs of sites compared | queries | Z3 decides | cvc5 decides | Bitwuzla decides (sorts as 64-bit vectors) |
|---|---|---|---|---|
| up to 100 | 58 | 38 | 23 | 45 |
| 101 to 1,000 | 52 | 29 | 42 | 41 |
| 1,001 to 10,000 | 20 | 5 | 12 | 14 |
| over 10,000 | 9 | 0 | 0 | 0 |

## Satisfiable answers read back (criterion 3)

| solver, encoding | query | read back as | queries |
|---|---|---|---|
| Z3, positional | `divergence` | Divergent | 26 |
| Z3, positional | `divergence` | Unknown(abstraction) | 42 |
| cvc5, sequence (control) | `divergence` | Divergent | 10 |
| cvc5, sequence (control) | `divergence` | Unknown(abstraction) | 27 |
| cvc5, sequence (control) | `divergence` | not counted: Z3 cannot complete the model | 14 |
| cvc5, sequence (control) | `opaque` | not counted: Z3 cannot complete the model | 1 |
| cvc5, positional | `divergence` | Divergent | 11 |
| cvc5, positional | `divergence` | Unknown(abstraction) | 32 |
| cvc5, positional | `divergence` | not counted: Z3 cannot complete the model | 31 |
| cvc5, positional | `opaque` | not counted: Z3 cannot complete the model | 1 |
| Bitwuzla, positional, sorts as 64-bit vectors | `divergence` | Divergent | 21 |
| Bitwuzla, positional, sorts as 64-bit vectors | `divergence` | Unknown(abstraction) | 34 |
| Bitwuzla, positional, sorts as 64-bit vectors | `divergence` | not counted: Z3 cannot complete the model | 42 |
| Bitwuzla, positional, sorts as 64-bit vectors | `opaque` | not counted: Z3 cannot complete the model | 3 |

No read-back was rejected by Z3 and no replay showed no difference. Taking the best positional
answer a query has from any of the three solvers: 33 Divergent, 4 `divergence` proved
unsatisfiable, 38 Unknown(abstraction), 49 satisfiable and not read back, 18 undecided. The 49 are
the spike's read-back, which asks Z3 the hard query again with some constants fixed, as in P1-025.

## Queries proved unsatisfiable
The four are the four cvc5 proved in the sequence form in P1-025. Rung 1's other queries were asked
of Z3 and, where Z3 gave up, of the other solvers.

| procedure identity | proved in the positional form by | rung 1's other queries |
|---|---|---|
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | Z3 88 ms, Bitwuzla 316 ms, cvc5 3,039 ms | pair not proved: `opaque` is satisfiable (Z3) |
| `GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::Delete_Click(object,System.EventArgs)` | Z3 333 ms, cvc5 25,389 ms, Bitwuzla 27,911 ms | pair not proved: `opaque` is satisfiable (Z3) |
| `GitUI.CommandsDialogs.RevisionFileTreeController::LoadChildren(GitUIPluginInterfaces.IGitItem,System.Windows.Forms.TreeNodeCollection,System.Windows.Forms.ImageList.ImageCollection)` | Z3 1,817 ms | pair not proved: `opaque` is satisfiable (cvc5) |
| `GitUI.UserControls.WaitSpinner::.ctor()` | Z3 36 ms, Bitwuzla 180 ms, cvc5 565 ms | pair not proved: `opaque` is satisfiable (cvc5) |

All four would be Unknown(opaque) in place of Unknown(timeout).

## What the answers are worth
- The trace's theories were what made half of these queries hard for Z3. With them gone Z3 moves 72
  of 142 `timeout` Unknowns to another answer with no second solver, no new dependency and less
  time than it spends giving up today (a median of 5.4 s on the ones it still gives up on). cvc5
  in the sequence form, which ADR 0050 adopts, moves 56.
- It proves no pair. P2-050 (a larger budget) and P1-025 (another solver) found the same. What
  these 142 results hold is divergences and abstractions, not hidden Equivalents: 33 replay to a
  Divergent.
- The positional form is quadratic in call sites. Nothing decides any of the 9 queries that compare
  more than 10,000 pairs of sites, and Z3 decides 5 of the 29 above 1,000.
- The spike measured only queries Z3 gives up on. What the positional form does to the queries Z3
  answers today in the sequence form was not measured; the ticket that builds it must.
- Bitwuzla decides the most (103), and 46 that Z3 in the positional form does not. It needs the
  sorts written as bit-vectors, which is a change to what is sent, not to the trace, and its
  satisfiable answers are the ones the spike's read-back loses most (42 of 100).

## Decisions
- Criterion 5 gates the follow-on ticket on some solver proving a pair, and none does. The ticket is
  written all the same (P1-038, rung 1's trace compared by position): the yield is 72 of 142 with
  Z3 alone, which is more than the 56 cvc5 gives in the encoding ADR 0050 adopted it for, and it
  costs no dependency. Recorded as a Deviation in P1-034's Notes.
- Bitwuzla is not adopted and ADR 0050 is not clarified: no pair is proved, so the gate holds for
  the part that adds a solver. ROADMAP's post-MVP list has the measured line.
- No README scoreboard number moves: nothing under `src/` changed.

## Per query
| procedure identity | query | Z3, sequence | Z3, positional | cvc5, sequence | cvc5, positional | Bitwuzla, as written | Bitwuzla, sorts as 64-bit vectors |
|---|---|---|---|---|---|---|---|
| `AppVeyorIntegration.AppVeyorAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | unknown |
| `BugReporter.Info.GeneralInfo::.ctor(BugReporter.Serialization.SerializableException)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `ConEmu.WinForms.ConEmuSession::Init_MakeConEmuCommandLine_EmitConfigFile(System.IO.DirectoryInfo,ConEmu.WinForms.ConEmuStartInfo,ConEmu.WinForms.ConEmuSession.HostContext)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `ConEmu.WinForms.GetInfoRoot::QueryAsync(ConEmu.WinForms.ConEmuSession)` | `divergence` | timeout | unsat | unsat | unsat | error | unsat |
| `GitCommands.AppSettings::.cctor()` | `opaque` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommands.AppSettings::LoadEncodings()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Divergent | error | sat: Divergent |
| `GitCommands.EnvironmentConfiguration::SetEnvironmentVariables()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Divergent | sat: Divergent | error | sat: Unknown(abstraction) |
| `GitCommands.Executable.ProcessWrapper::.ctor(string,string,string,bool,bool,bool,System.Text.Encoding,bool)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommands.ExecutableExtensions::ExecuteAsync(GitUIPluginInterfaces.IExecutable,GitExtUtils.ArgumentString,System.Action<global::System.IO.StreamWriter>,System.Text.Encoding,GitCommands.CommandCache,bool)` | `divergence` | unknown | sat: Unknown(abstraction) | error | error | unknown | unknown |
| `GitCommands.Git.GitDirectoryResolver::Resolve(string)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitCommands.Git.Tag.GitTagController::CreateTag(GitCommands.Git.Commands.GitCreateTagArgs,System.Windows.Forms.IWin32Window)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitCommands.GitModule::CommitCmd(bool,bool,string,bool,bool,bool,string,bool)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | unknown |
| `GitCommands.GitModule::GetConflictsAsync(string)` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | error | unknown |
| `GitCommands.GitModule::GetFetchArgs(string,string,string,bool?,bool,bool,bool)` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | error | sat: Divergent |
| `GitCommands.GitModule::GetInteractiveRebasePatchFiles()` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommands.GitModule::HandleConflictSelectSide(string,string)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Divergent | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitCommands.GitModule::HandleConflictsSaveSide(string,string,string)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitCommands.GitModule::SaveBlobAs(string,string)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitCommands.Patches.Chunk::ParseChunk(string,int,int,int)` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommands.Patches.PatchProcessor::CreatePatchFromString(string[],System.Lazy<global::System.Text.Encoding>,ref int)` | `divergence` | timeout | unknown | error | error | error | unknown |
| `GitCommands.PathUtil::FindInFolders(string,System.Collections.Generic.IEnumerable<string>)` | `divergence` | unknown | sat: Unknown(abstraction) | timeout | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitCommands.Settings.FileSettingsCache::SaveImpl()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitCommands.UserRepositoryHistory.RecentRepoSplitter::AddToOrderedMiddleDots(System.Collections.Generic.SortedList<string, global::System.Collections.Generic.List<global::GitCommands.UserRepositoryHistory.RecentRepoInfo>>,GitCommands.UserRepositoryHistory.RecentRepoInfo)` | `divergence` | timeout | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | unknown | sat: Unknown(abstraction) |
| `GitCommandsTests.CommitDataManagerTest::CreateFromFormattedData()` | `divergence` | unknown | unknown | error | error | error | unknown |
| `GitCommandsTests.CommitMessageManagerTests::AmendState_should_be_false_if_file_contains(string)` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.CommitMessageManagerTests::AmendState_should_be_true_if_file_contains(string)` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.CommitMessageManagerTests::Setup()` | `opaque` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.CommitTemplateManagerTests::LoadGitCommitTemplate_should_load_file_content()` | `divergence` | unknown | sat: Divergent | error | error | error | unknown |
| `GitCommandsTests.EnvironmentPathsProviderTests::GetEnvironmentValidPaths()` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Unknown(abstraction) |
| `GitCommandsTests.FileAssociatedIconProviderTests::Get_should_add_entry_for_extension_once()` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.Git.GitDirectoryResolverTests::Resolve_submodule_real_filesystem()` | `divergence` | unknown | sat: Divergent | error | error | error | sat: Divergent |
| `GitCommandsTests.Git.GitDirectoryResolverTests::Setup()` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.Git.Gpg.GitGpgControllerTests::Validate_GetTagVerifyMessage(int,string)` | `divergence` | unknown | unknown | error | error | error | unknown |
| `GitCommandsTests.Git.IndexLockManagerTests::Resolve_submodule_real_filesystem()` | `divergence` | unknown | unknown | error | error | error | unknown |
| `GitCommandsTests.Git.IndexLockManagerTests::Setup()` | `divergence` | unknown | unknown | error | error | error | unknown |
| `GitCommandsTests.Git.SubmoduleHelpersTest::GetSubmoduleNamesFromDiffTest()` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.GitModuleTests::ParseGitBlame()` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.GitRevisionInfoProviderTests::LoadChildren_should_return_shallow_tree_for_GitItem_with_updated_FileName()` | `divergence` | unknown | sat: Divergent | error | error | error | unknown |
| `GitCommandsTests.Helpers.PathUtilTest::DeleteWithExtremePrejudice_should_return_true_if_delete_successful()` | `divergence` | unknown | sat: Divergent | error | error | unknown | sat: Divergent |
| `GitCommandsTests.Remote.ConfigFileRemoteSettingsManagerTests::CreateSubstituteRef(string,string,string)` | `divergence` | unknown | sat: Divergent | error | error | error | sat: Divergent |
| `GitCommandsTests.Remote.ConfigFileRemoteSettingsManagerTests::LoadRemotes_should_not_populate_remotes_if_none()` | `divergence` | unknown | sat: Unknown(abstraction) | error | error | unknown | sat: Unknown(abstraction) |
| `GitCommandsTests.RevisionReaderTests::ParseCommitBody_should_work(string,string,string)` | `divergence` | unknown | sat: Divergent | error | error | unknown | unknown |
| `GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent()` | `divergence` | unknown | sat: Divergent | error | error | unknown | unknown |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_second_nested_module_changes()` | `divergence` | timeout | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_top_module_changes()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | `divergence` | timeout | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_prechanges_noupdate()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_second_nested_module_changes()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change_commit()` | `divergence` | timeout | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_commit_second_nested_module_change()` | `divergence` | timeout | unknown | error | error | error | unknown |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_no_forced_changes()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_second_nested_module_change()` | `divergence` | timeout | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_change()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_changes()` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::Delete_Click(object,System.EventArgs)` | `divergence` | unknown | unsat | unsat | unsat | unknown | unsat |
| `GitExtensions.Plugins.FindLargeFiles.FindLargeFilesForm::FindLargeFilesFunction()` | `divergence` | timeout | unknown | timeout | timeout | error | unknown |
| `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()` | `divergence` | unknown | unknown | timeout | timeout | error | timeout |
| `GitExtensions.Plugins.Gource.GourcePlugin::Execute(GitUIPluginInterfaces.GitUIEventArgs)` | `divergence` | unknown | unknown | timeout | timeout | error | sat: Z3 cannot complete the model |
| `GitExtensions.Plugins.ReleaseNotesGenerator.ReleaseNotesGeneratorForm::buttonGenerate_Click(object,System.EventArgs)` | `divergence` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitExtensions.Program::GetWorkingDir(string[])` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitExtensions.Program::HandleConfigurationException(System.Configuration.ConfigurationException)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.AutoCompletion.CommitAutoCompleteProvider::GetAutoCompleteWordsAsync(System.Threading.CancellationToken)` | `divergence` | unknown | unknown | timeout | timeout | timeout | timeout |
| `GitUI.Avatars.GravatarProvider::GetAvatarAsync(string,string,int)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormBrowse::FillUserShells(string)` | `divergence` | unknown | unknown | sat: Unknown(abstraction) | sat: Z3 cannot complete the model | unknown | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormClone::OkClick(object,System.EventArgs)` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormClone::OnRuntimeLoad(System.EventArgs)` | `divergence` | timeout | unknown | timeout | timeout | unknown | timeout |
| `GitUI.CommandsDialogs.FormClone::ToTextUpdate(object,System.EventArgs)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormCommit::CommitMessageToolStripMenuItemDropDownOpening(object,System.EventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | error | error | error | unknown |
| `GitUI.CommandsDialogs.FormCommit::ResetSoftClick(object,System.EventArgs)` | `divergence` | unknown | unknown | timeout | timeout | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormDeleteBranch::FormDeleteBranchLoad(object,System.EventArgs)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | unknown | unknown |
| `GitUI.CommandsDialogs.FormFileHistory::saveAsToolStripMenuItem_Click(object,System.EventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormFormatPatch::FormatPatch_Click(object,System.EventArgs)` | `divergence` | timeout | unknown | timeout | timeout | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormInit::InitClick(object,System.EventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormPull::BranchesDropDown(object,System.EventArgs)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | timeout | unknown | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormPush::PushChanges(System.Windows.Forms.IWin32Window)` | `divergence` | timeout | unknown | timeout | timeout | error | timeout |
| `GitUI.CommandsDialogs.FormRemotes::SaveClick(object,System.EventArgs)` | `divergence` | unknown | unknown | timeout | timeout | unknown | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormResolveConflicts::InitMergetool()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Z3 cannot complete the model | sat: Unknown(abstraction) | error | unknown |
| `GitUI.CommandsDialogs.FormResolveConflicts::SaveAs(string)` | `divergence` | unknown | sat: Unknown(abstraction) | timeout | sat: Unknown(abstraction) | error | sat: Divergent |
| `GitUI.CommandsDialogs.FormResolveConflicts::TryMergeWithScript(string,string,string,string)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.FormResolveConflicts::UseMergeWithScript(string,string,string,string,string)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.FormVerify.LostObject::TryParse(GitCommands.GitModule,string)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.RevisionDiffControl::DeleteSelectedFiles()` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.RevisionDiffControl::saveAsToolStripMenuItem1_Click(object,System.EventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | timeout | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.RevisionFileTreeController::LoadChildren(GitUIPluginInterfaces.IGitItem,System.Windows.Forms.TreeNodeCollection,System.Windows.Forms.ImageList.ImageCollection)` | `divergence` | unknown | unsat | unsat | timeout | error | timeout |
| `GitUI.CommandsDialogs.SettingsDialog.FormAvailableEncodings::LoadEncoding()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Z3 cannot complete the model | sat: Unknown(abstraction) | unknown | sat: Divergent |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.BuildServerIntegrationSettingsPage::CreateBuildServerSettingsUserControl()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ChecklistSettingsPage::ShellExtensionsRegistered_Click(object,System.EventArgs)` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | error | sat: Divergent |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.FormFixHome::IsFixHome()` | `divergence` | unknown | sat: Divergent | error | error | error | sat: Divergent |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.FormFixHome::LoadSettings()` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ShellExtensionSettingsPage::InitializeComponent()` | `divergence` | timeout | unknown | error | error | error | unknown |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.SshSettingsPage::AutoFindPuttyPathsInDir(string)` | `divergence` | unknown | sat: Divergent | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::Initialize()` | `divergence` | unknown | unknown | timeout | timeout | error | timeout |
| `GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::Worktrees_CellClick(object,System.Windows.Forms.DataGridViewCellEventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Divergent | sat: Divergent | error | sat: Divergent |
| `GitUI.CommitInfo.CommitInfo::.ctor()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.CommitInfo.CommitInfo::GetSortedTags()` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.Editor.BlameAuthorMargin::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `GitUI.Editor.Diff.DiffLineNumAnalyzer::Analyze(string)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | timeout |
| `GitUI.Editor.FileViewer::CopyNotStartingWith(char)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | unknown |
| `GitUI.Editor.FileViewer::ResetView(GitUI.Editor.FileViewer.ViewMode,string,GitUI.UserControls.FileStatusItem,string)` | `divergence` | unknown | sat: Unknown(abstraction) | timeout | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | `divergence` | timeout | unknown | timeout | timeout | error | sat: Z3 cannot complete the model |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessEndElement(System.Xml.XmlReader,GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension.RTFCurrentState,System.Windows.Forms.RichTextBox)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::SetXHTMLText(System.Windows.Forms.RichTextBox,string)` | `divergence` | timeout | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `GitUI.FileStatusList::IsFilterMatch(GitCommands.GitItemStatus)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | `divergence` | timeout | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.Infrastructure.Telemetry.DiagnosticsClient::Initialize(bool)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Divergent |
| `GitUI.RevisionGridControl::goToMergeBaseCommitToolStripMenuItem_Click(object,System.EventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | timeout | sat: Unknown(abstraction) | error | sat: Unknown(abstraction) |
| `GitUI.Script.ScriptOptionsParser::ParseScriptArguments(string,string,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl,GitUIPluginInterfaces.IGitModule,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.GitRevision>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<string>,GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,GitUIPluginInterfaces.GitRevision,string)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | sat: Unknown(abstraction) |
| `GitUI.UserControls.EditboxBasedConsoleOutputControl::StartProcess(string,string,string,System.Collections.Generic.Dictionary<string, string>)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `GitUI.UserControls.RevisionGrid.Columns.MessageColumnProvider::DrawCommitMessage(System.Windows.Forms.DataGridViewCellPaintingEventArgs,GitUIPluginInterfaces.GitRevision,GitUI.UserControls.RevisionGrid.CellStyle,System.Drawing.Rectangle,GitUI.UserControls.RevisionGrid.Columns.MultilineIndicator,ref int)` | `divergence` | unknown | sat: Divergent | sat: Unknown(abstraction) | sat: Divergent | unknown | sat: Divergent |
| `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::Init(System.Collections.Generic.IReadOnlyList<global::GitUI.UserControls.RevisionGrid.FormQuickItemSelector.ItemData>,string)` | `divergence` | timeout | unknown | timeout | timeout | unknown | sat: Z3 cannot complete the model |
| `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::InitializeComponent()` | `divergence` | unknown | unknown | error | error | error | unknown |
| `GitUI.UserControls.RevisionGrid.IndexWatcher::SetFileSystemWatcher()` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | unknown | sat: Divergent |
| `GitUI.UserControls.WaitSpinner::.ctor()` | `divergence` | unknown | unsat | unsat | unsat | error | unsat |
| `GitUIPluginInterfaces.ManagedExtensibility::CreateExportProvider(string)` | `divergence` | unknown | sat: Divergent | error | error | error | unknown |
| `GitUITests.Avatars.AvatarPersistentCacheTests::ClearCacheAsync_should_remove_all()` | `divergence` | unknown | sat: Divergent | error | error | error | unknown |
| `GitUITests.Avatars.AvatarPersistentCacheTests::GetAvatarAsync_uses_inner_if_file_expired()` | `opaque` | unknown | unknown | error | error | error | sat: Z3 cannot complete the model |
| `GitUITests.Editor.Diff.LinePrefixHelperFixture::PreDocumentForDiffText(string)` | `divergence` | unknown | sat: Unknown(abstraction) | error | error | unknown | sat: Unknown(abstraction) |
| `GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports()` | `divergence` | unknown | sat: Divergent | error | error | error | sat: Divergent |
| `ICSharpCode.TextEditor.Actions.ToggleLineComment::RemoveCommentAt(ICSharpCode.TextEditor.Document.IDocument,string,ICSharpCode.TextEditor.Document.ISelection,int,int)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Document.DefaultHighlightingStrategy::ParseLine(ICSharpCode.TextEditor.Document.IDocument)` | `divergence` | unknown | unknown | timeout | timeout | timeout | timeout |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | sat: Z3 cannot complete the model |
| `ICSharpCode.TextEditor.Document.TextUtilities::GetExpressionBeforeOffset(ICSharpCode.TextEditor.TextArea,int)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Divergent | sat: Divergent | error | sat: Unknown(abstraction) |
| `ICSharpCode.TextEditor.Gui.CompletionWindow.AbstractCompletionWindow::AddShadowToWindow(System.Windows.Forms.CreateParams)` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | unknown | sat: Divergent |
| `ICSharpCode.TextEditor.TextAreaControl::HandleMouseWheel(System.Windows.Forms.MouseEventArgs)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Unknown(abstraction) | sat: Unknown(abstraction) | unknown | sat: Unknown(abstraction) |
| `ICSharpCode.TextEditor.Util.TipSection::SetRequiredSize(System.Drawing.SizeF)` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | unknown | sat: Divergent |
| `ICSharpCode.TextEditor.Util.TipSplitter::OnMaximumSizeChanged()` | `divergence` | timeout | unknown | timeout | sat: Z3 cannot complete the model | unknown | timeout |
| `JenkinsIntegration.JenkinsAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | `divergence` | unknown | unknown | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | error | unknown |
| `NetSpell.SpellChecker.Dictionary.WordDictionary::Initialize()` | `divergence` | timeout | unknown | error | error | error | unknown |
| `NetSpell.SpellChecker.Dictionary.WordDictionary::LoadUserFile()` | `divergence` | unknown | sat: Unknown(abstraction) | error | error | error | unknown |
| `NetSpell.SpellChecker.Dictionary.WordDictionary::SaveUserFile()` | `divergence` | unknown | sat: Divergent | error | error | error | unknown |
| `ReleaseNotesGeneratorTests.GitLogLineParserTests::Parse_lines_should_parse_correctly()` | `divergence` | unknown | unknown | error | error | error | unknown |
| `ResourceManager.CommitDataRenders.CommitDataHeaderRenderer::Render(GitCommands.CommitData,bool)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `ResourceManager.CommitDataRenders.CommitDataHeaderRenderer::RenderPlain(GitCommands.CommitData)` | `divergence` | unknown | sat: Unknown(abstraction) | sat: Z3 cannot complete the model | sat: Z3 cannot complete the model | unknown | sat: Unknown(abstraction) |
| `ResourceManager.LocalizationHelpers::ProcessSubmoduleStatus(GitCommands.GitModule,GitCommands.GitSubmoduleStatus,bool,bool)` | `divergence` | timeout | unknown | error | error | error | unknown |
| `ResourceManager.Translator::GetTranslation(string)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | unknown | sat: Z3 cannot complete the model |
| `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Setup()` | `divergence` | unknown | unknown | error | error | unknown | sat: Z3 cannot complete the model |
| `TeamCityIntegration.TeamCityAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | `divergence` | unknown | unknown | timeout | sat: Z3 cannot complete the model | error | unknown |
| `TranslationApp.Program::Main()` | `divergence` | unknown | sat: Divergent | sat: Divergent | sat: Divergent | error | sat: Z3 cannot complete the model |
