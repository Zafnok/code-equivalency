# P2-047 Divergent audit (2026-09-30)

How often is a Divergent result right? This audit adjudicates a fixed set of 77 Divergent results
from P2-046's plain `full` runs (`docs/runs/2026-09-30-full-verdict.md`, equiv `bd8e379`) and
reports Divergent precision. Nothing under `src/` or `tests/` was changed. It records identities,
rules and classifications only, per `docs/runs/README.md`.

## Audit set

| Pair | Run | EQ002 in run | EQ006 in run | Audited |
|---|---|---|---|---|
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | `20260929-1608-full` | 1 | 16 | all 17 |
| pmb-shiningrush__serviceant | `20260929-1608-full` | 0 | 0 | none to audit |
| pmb-lethek__signalr.extras.autofac | `20260929-1608-full` | 0 | 0 | none to audit |
| gitextensions-8522 | `20260929-1918-full-rerun` | 84 | 275 | 30 EQ002 + 30 EQ006 |

Git Extensions sample: for each rule, the results were sorted by `resultFingerprint/v1`, shuffled
with Python's `random.Random(47).shuffle`, and the first 30 kept. The 12-character fingerprint
prefix of every audited result is in the appendix.

ServiceAnt's 4 EQ002 that the ticket names came from M4-007. In P2-046 the same pair has 0 EQ002 and
0 EQ006 (its changed pairs fell from 23 to 2), so ServiceAnt contributes nothing to this audit.

## Method

- **Replay** came from the matching `--execute` run (`20260929-1609-full-execute` and
  `20260929-1923-full-execute-rerun`). Of the 77, 1 is `reproduced` and 76 are `not-constructible`:
  "the divergence is in the call trace, which replay does not observe", "not public", "no public
  parameterless constructor", "the model constrains ...", "generic". None is `not-reproduced`, and
  none is `proofMethod: observed`.
- **Hand trace**: the legacy and modern bodies were diffed and the model was read against them. A
  result is a false positive when the traced input cannot produce the claimed difference on the real
  runtimes.
- **Test**: one result was confirmed by a test under `.corpus/audit/P2-047/`. It compiles the
  unmodified legacy source with the .NET Framework 4.8 compiler and loads the built modern net10.0
  assembly. It passes on legacy and fails on modern. It is not committed.
- Decision: for EQ006, the claim is "the method reaches a runtime change documented for this API".
  EQ006's model havocs the API's result, so its concrete values are not a witness by design. An EQ006
  is a false positive when the documented change cannot be reached through the call's arguments. It is
  undetermined when the change can be reached but nothing ran it, even if the model's own values are
  impossible (for example the GDI+ rows, where the model has one side return and the other throw,
  while the documented change only swaps the exception type). For EQ002 the model's input is the
  witness.
- Decision: `GetHashCode` results whose value differs between runtimes are undetermined, not false
  positives. The method returns a different number for the same input, so the difference is reachable.
  That no caller persists the hash is a question of what a user cares about, not of correctness. P2-064's
  review list is where they would be grouped and ranked down.

## Results

### pmb-tomasjohansson__adapters-shortest-paths-dotnet (agent)

| Rule | Confirmed | False positive | Undetermined | Total | Precision |
|---|---|---|---|---|---|
| EQ002 | 0 | 1 | 0 | 1 | 0% (0 of 1) |
| EQ006 | 1 | 9 | 6 | 16 | 10.0% (1 of 10) |
| **All** | 1 | 10 | 6 | 17 | **9.1% (1 of 11)** |

### gitextensions-8522 (human; 30 + 30 sampled)

| Rule | Confirmed | False positive | Undetermined | Total | Precision |
|---|---|---|---|---|---|
| EQ002 | 1 | 26 | 3 | 30 | 3.7% (1 of 27) |
| EQ006 | 0 | 15 | 15 | 30 | 0% (0 of 15) |
| **All** | 1 | 41 | 18 | 60 | **2.4% (1 of 42)** |

### Overall

| Rule | Confirmed | False positive | Undetermined | Total | Precision |
|---|---|---|---|---|---|
| EQ002 | 1 | 27 | 3 | 31 | 3.6% (1 of 28) |
| EQ006 | 1 | 24 | 21 | 46 | 4.0% (1 of 25) |
| **All** | 2 | 51 | 24 | 77 | **3.8% (2 of 53)** |

Divergent precision = confirmed / (confirmed + false positive) = **3.8%**. Even if every undetermined
result were real, precision would be at most 33.8% (26 of 77). About two in three Divergent results
in this set are wrong, and the rest are mostly real differences that no oracle executed.

Undetermined, by obstacle: 10 real by hand trace but not executed (5 with no legacy build, 4
non-public, 1 UI handler), 6 depend on a native GDI+ failure, 4 are `GetHashCode` (real by
construction), 2 have no ICU/NLS input found, 1 depends on an environment variable and 1 on the
process's current directory.

## False-positive causes

Every distinct cause is one ticket. No existing ticket owned any of them.

| Ticket | Cause | Rule | False positives |
|---|---|---|---|
| P2-068 | A call to a solution-local one-line forwarder is replaced by the BCL member it forwards to; the two identities are unrelated uninterpreted functions and the trace differs | EQ002 | 11 |
| P2-069 | An unchanged call site binds to a different symbol after a dependency upgrade (a property retyped from a class to an interface, a new generic instantiation, a generated class moved namespace) | EQ002 | 9 |
| P2-070 | Identical source binds to a different BCL overload or declaring type under the new reference assemblies (`TrimEnd(char[])` to `TrimEnd(char)`, `DirectoryInfo` to `FileSystemInfo`) | EQ002 | 3 |
| P2-071 | An effect-free BCL call (a pure getter, an allocation-only constructor) that differs between the sides counts as an observable call-trace difference, and its uninterpreted `threw` is free | EQ002 | 3 |
| P2-072 | Two identical bodies with a `try` whose `catch` rethrows lower to different call traces: one side omits the `try` block's calls | EQ002 | 1 |
| P2-073 | An EQ006 row fires for a call whose constant arguments cannot reach the documented change (a regex pattern without case-insensitive ranges, a four-digit-year format, an ASCII constant compared under culture rules, a constant path or bitmap size, a serialized type without `[Obsolete]` members) | EQ006 | 15 |
| P2-074 | An EQ006 path row fires on a path that comes from a BCL member that only yields valid paths (`Assembly.Location`, the test directory, the app-data folder), or that a guard validated first | EQ006 | 7 |
| P2-075 | An EQ006 row matches a member or overload that its documented change does not affect (an `Ordinal` comparison under the ICU row; reading `ListView.Groups` under the group-ownership row) | EQ006 | 2 |

Four of the eight causes (P2-068 to P2-071, 26 of 51 false positives) share one root: a call site
that means the same thing on both sides is given two different callee identities, and ADR 0018 makes
both the identity and the call's outcomes observable. P2-073 to P2-075 (24) are all EQ006 rows that
match on the member alone. P2-072 (1) is a lowering difference on identical source.

## Appendix: every audited result

Columns: row, pair, rule, procedure identity, `resultFingerprint/v1` prefix, classification, and the
owning ticket (false positive), the basis (confirmed) or the obstacle (undetermined).

| # | Pair | Rule | Procedure | Fingerprint | Classification | Ticket, basis or obstacle |
|---|---|---|---|---|---|---|
| 0 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Core.Impl.VertexImpl::GetHashCode() `` | `6d3911c52437` | undetermined | real by construction (hash differs per runtime), not executed |
| 1 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Core.Impl.Generics.EdgeGenericsImpl`2::GetHashCode() `` | `edb3373513fd` | undetermined | real by construction (hash differs per runtime), not executed |
| 2 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Core.Parsers.EdgeParser`3::FromStringToEdge(string) `` | `20c2fd541fb6` | false positive | P2-073 |
| 3 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.StringUtility::GetMultilineStringAsListOfTrimmedStringsIgnoringLinesWithOnlyWhiteSpace(string) `` | `b051980f5b83` | false positive | P2-073 |
| 4 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.StringUtility::GetDoubleAsStringWithoutZeroesAndDotIfNotRelevant(string) `` | `7c73513d23ce` | false positive | P2-073 |
| 5 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.StringUtility::GetDoubleAsStringWithoutZeroesAndDotIfNotRelevant(double) `` | `e73bd1989be0` | confirmed | test under `.corpus/` |
| 6 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.ResourceReader::GetFilesInResourcesFolder(string) `` | `2df76f6db46f` | undetermined | real difference by hand trace, not executed: no legacy build |
| 7 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.ResourceReader::GetFileInResourcesFolder(string) `` | `f8bcfb66bcb9` | undetermined | real difference by hand trace, not executed: no legacy build |
| 8 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.ResourceReader::GetAbsolutePathToResourceFolder() `` | `6b7aeb98691c` | false positive | P2-074 |
| 9 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Utils.TimeMeasurerTest::CreateDateTime(string) `` | `47698b97d021` | false positive | P2-073 |
| 10 | Tomas | EQ006 | `` edu.asu.emit.algorithm.graph.Graph::AddEdgeFromStringWithEdgeNamesAndWeight(string) `` | `0acfbaef7d79` | false positive | P2-073 |
| 11 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Adaptee.YanQi.Test.YenTopKShortestPathsAlgTest::GetExpectedWeightAndNodes(string) `` | `d94040f38ddf` | false positive | P2-073 |
| 12 | Tomas | EQ006 | `` edu.ufl.cise.bsmock.graph.Graph::ReadFromFile(string) `` | `83fe8c23ad01` | undetermined | real difference by hand trace, not executed: no legacy build |
| 13 | Tomas | EQ006 | `` TestYen::GetModifiedPath(string) `` | `f62467161723` | false positive | P2-073 |
| 14 | Tomas | EQ006 | `` java.util.ListItemForTest::GetHashCode() `` | `8f982349891d` | undetermined | real by construction (hash differs per runtime), not executed |
| 15 | Tomas | EQ006 | `` Programmerare.ShortestPaths.Example.Roadrouting.Database.PersistenceSessionFactory::GetFullPath(string) `` | `ab3c565d865b` | false positive | P2-074 |
| 16 | Tomas | EQ002 | `` Programmerare.ShortestPaths.Example.Roadrouting.Database.PersistenceSessionFactory::TryGetSQLiteSubDirectoryOfProjectRoot(System.IO.DirectoryInfo) `` | `f29c018fed8c` | false positive | P2-070 |
| 17 | Git Extensions | EQ002 | `` EasyHook.LocalHook::Create(System.IntPtr,System.Delegate,object) `` | `e7f77e34249f` | false positive | P2-072 |
| 18 | Git Extensions | EQ002 | `` EasyHook.LocalHook::Release() `` | `c81407bf2277` | confirmed | replay `reproduced` |
| 19 | Git Extensions | EQ002 | `` ICSharpCode.TextEditor.TextAreaControl::JumpTo(int) `` | `50d71b9d5605` | false positive | P2-070 |
| 20 | Git Extensions | EQ002 | `` GitUI.GitExtensionsDialog::OnHelpButtonClicked(System.ComponentModel.CancelEventArgs) `` | `150d90a7ef48` | false positive | P2-068 |
| 21 | Git Extensions | EQ002 | `` GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string) `` | `503adef9567f` | undetermined | real difference by hand trace, not executed: non-public |
| 22 | Git Extensions | EQ002 | `` GitCommands.Git.IndexLockManager::DeleteIndexLock(string) `` | `37a349b60f4c` | false positive | P2-069 |
| 23 | Git Extensions | EQ002 | `` GitUITests.Editor.RichTextBoxXhtmlSupportExtensionTests::GetLink_should_return_null_if_right_of_link() `` | `db0e86a4e14f` | false positive | P2-071 |
| 24 | Git Extensions | EQ002 | `` GitUI.Theming.ThemeLoader::ParseRule(string,ExCSS.StyleRule,System.Collections.Generic.IReadOnlyList<string>,GitUI.Theming.ThemeLoader.ThemeColors) `` | `a0234634fba5` | undetermined | real difference by hand trace, not executed: non-public |
| 25 | Git Extensions | EQ002 | `` GitUI.OsShellUtil::OpenUrlInDefaultBrowser(string) `` | `55c6f914a5f5` | false positive | P2-068 |
| 26 | Git Extensions | EQ002 | `` GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string() `` | `2c004ccbcf43` | false positive | P2-069 |
| 27 | Git Extensions | EQ002 | `` ResourceManager.LinkFactory::AddLink(string,string) `` | `9eff42b68a6e` | undetermined | real difference by hand trace, not executed: non-public |
| 28 | Git Extensions | EQ002 | `` GitCommands.FileHelper::IsBinaryFileName(GitCommands.GitModule,string) `` | `6bd4203f15b9` | false positive | P2-068 |
| 29 | Git Extensions | EQ002 | `` GitUIPluginInterfaces.NumberSetting`1::ConvertFromString(string) `` | `aae05728702a` | false positive | P2-068 |
| 30 | Git Extensions | EQ002 | `` GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::IsCurrentlyOpenedWorktree(GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree.WorkTree) `` | `e92a89bef8e4` | false positive | P2-070 |
| 31 | Git Extensions | EQ002 | `` GitCommandsTests.SshPathLocatorTest::SetUpFileSystemWithSshExePathsAs(string[]) `` | `027c59e75db8` | false positive | P2-069 |
| 32 | Git Extensions | EQ002 | `` GitUI.UserControls.AuthorRevisionHighlighting::IsHighlighted(GitUIPluginInterfaces.GitRevision) `` | `64e17ad9e449` | false positive | P2-068 |
| 33 | Git Extensions | EQ002 | `` GitCommands.DiffMergeTools.DiffMergeToolConfigurationManager::UnquoteString(string) `` | `e2ca4a68acb6` | false positive | P2-068 |
| 34 | Git Extensions | EQ002 | `` GitCommands.DiffMergeTools.DiffMergeToolConfigurationManager::GetToolCommand(string,GitCommands.DiffMergeTools.DiffMergeToolType) `` | `ae7c13a233bb` | false positive | P2-068 |
| 35 | Git Extensions | EQ002 | `` GitCommands.FileAssociatedIconProvider::DeleteFile(string) `` | `96be71922a81` | false positive | P2-069 |
| 36 | Git Extensions | EQ002 | `` GitCommandsTests.FileAssociatedIconProviderTests::Setup() `` | `2bbc03395e0e` | false positive | P2-069 |
| 37 | Git Extensions | EQ002 | `` GitCommands.CommitMessageManager::ResetCommitMessage() `` | `080a053199ce` | false positive | P2-069 |
| 38 | Git Extensions | EQ002 | `` GitCommands.CommitMessageManager::ReadFile(string,string,System.Text.Encoding) `` | `147d39e9db4a` | false positive | P2-069 |
| 39 | Git Extensions | EQ002 | `` GitCommands.Git.GitBranchNameOptions::.ctor(string) `` | `6fd3c576f538` | false positive | P2-068 |
| 40 | Git Extensions | EQ002 | `` GitUI.Theming.ThemeLoader::.ctor(GitUI.Theming.IThemeCssUrlResolver,GitUI.Theming.IThemeFileReader) `` | `16ac76b27c26` | false positive | P2-071 |
| 41 | Git Extensions | EQ002 | `` GitCommands.Utils.EnvUtils::ReplaceLinuxNewLinesDependingOnPlatform(string) `` | `324b78570f11` | false positive | P2-068 |
| 42 | Git Extensions | EQ002 | `` System.StringExtensions::CommonPrefix(string,string) `` | `cd18bfe83439` | false positive | P2-068 |
| 43 | Git Extensions | EQ002 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int) `` | `dccd0b400bcc` | false positive | P2-071 |
| 44 | Git Extensions | EQ002 | `` GitCommandsTests.Git.GitRevisionTesterTests::Setup() `` | `f0903f8bdfad` | false positive | P2-069 |
| 45 | Git Extensions | EQ002 | `` ConEmu.WinForms.ConEmuStartInfo::get_BaseConfiguration() `` | `84d3be4ad52c` | false positive | P2-069 |
| 46 | Git Extensions | EQ002 | `` GitCommands.PathUtil::EnsureTrailingPathSeparator(string) `` | `a9665cf81067` | false positive | P2-068 |
| 47 | Git Extensions | EQ006 | `` GitCommands.UserRepositoryHistory.RepositoryXmlSerialiser::Deserialize(string) `` | `8e793614f870` | false positive | P2-073 |
| 48 | Git Extensions | EQ006 | `` GitUI.Script.ScriptOptionsParser::GetRemotePath(string) `` | `b8102573206e` | undetermined | real difference by hand trace, not executed: no legacy build |
| 49 | Git Extensions | EQ006 | `` GitUI.CommandsDialogs.SettingsDialog.Pages.ChecklistSettingsPage::CheckGitCredentialWinStore() `` | `6ef931d59eb2` | false positive | P2-075 |
| 50 | Git Extensions | EQ006 | `` ICSharpCode.TextEditor.Document.FontContainer::ParseFont(string) `` | `7aeea614c107` | undetermined | real difference by hand trace, not executed: no legacy build |
| 51 | Git Extensions | EQ006 | `` GitCommands.ExecutableExtensions::CleanString(bool,string) `` | `6e26337dfcfb` | false positive | P2-073 |
| 52 | Git Extensions | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateLocal(GitCommands.GitModule,GitCommands.Settings.ConfigFileSettings,GitUIPluginInterfaces.SettingLevel,bool) `` | `556b80db7ec5` | false positive | P2-074 |
| 53 | Git Extensions | EQ006 | `` GitCommands.Settings.ConfigFileSettings::CreateGlobal(GitCommands.Settings.ConfigFileSettings,bool) `` | `e94c6bd7a21e` | undetermined | depends on process environment |
| 54 | Git Extensions | EQ006 | `` GitCommands.Git.GitBranchNameNormaliser::Rule10(string,GitCommands.Git.GitBranchNameOptions) `` | `b135ee3801b8` | false positive | P2-073 |
| 55 | Git Extensions | EQ006 | `` ICSharpCode.TextEditor.IconBarMargin::FillArrow(System.Drawing.Graphics,System.Drawing.Brush,System.Drawing.Rectangle) `` | `cb3f8868c835` | undetermined | depends on a native GDI+ failure |
| 56 | Git Extensions | EQ006 | `` GitExtensions.Plugins.GitStatistics.PieChart.PieSlice::DrawTop(System.Drawing.Graphics) `` | `6a9441afe1fb` | undetermined | depends on a native GDI+ failure |
| 57 | Git Extensions | EQ006 | `` ICSharpCode.TextEditor.Gui.InsightWindow.InsightWindow::OnPaintBackground(System.Windows.Forms.PaintEventArgs) `` | `8a98596bff84` | undetermined | depends on a native GDI+ failure |
| 58 | Git Extensions | EQ006 | `` NetSpell.SpellChecker.Spelling::CheckString(string) `` | `83131754a57d` | false positive | P2-073 |
| 59 | Git Extensions | EQ006 | `` GitUI.Theming.ButtonRenderer::RenderGroupBox(GitUI.Theming.ThemeRenderer.Context,System.Drawing.Rectangle) `` | `ce98ed36a46f` | undetermined | depends on a native GDI+ failure |
| 60 | Git Extensions | EQ006 | `` GitCommands.Remotes.RemoteParser::MatchRegExes(string,string[]) `` | `31889cfaa7bb` | false positive | P2-073 |
| 61 | Git Extensions | EQ006 | `` GitCommandsTests.Git.Tag.GitTagControllerTest::Setup() `` | `bca1ea8f53e3` | false positive | P2-074 |
| 62 | Git Extensions | EQ006 | `` GitCommands.AppSettings::get_AvatarImageCachePath() `` | `18c89186ead2` | false positive | P2-074 |
| 63 | Git Extensions | EQ006 | `` GitUI.FileStatusList::SelectNextVisibleItem() `` | `afd30fc318eb` | false positive | P2-075 |
| 64 | Git Extensions | EQ006 | `` ResourceManager.Xliff.TranslationSerializer::Deserialize(string) `` | `afaaf44b6352` | false positive | P2-074 |
| 65 | Git Extensions | EQ006 | `` ICSharpCode.TextEditor.Util.TipText::OnMaximumSizeChanged() `` | `2df86c6fb516` | undetermined | depends on a native GDI+ failure |
| 66 | Git Extensions | EQ006 | `` GitUITests.Theming.AppColorDefaultsTests::OneTimeSetUp() `` | `e89c3b5cb511` | false positive | P2-074 |
| 67 | Git Extensions | EQ006 | `` GitUI.Avatars.CustomAvatarProvider::FromTemplateSegment(GitUI.Avatars.IAvatarDownloader,string) `` | `4e013ea8d9d1` | undetermined | no differing ICU/NLS input found; non-public |
| 68 | Git Extensions | EQ006 | `` GitCommands.GitModule::ApplyPatch(string,GitExtUtils.ArgumentString) `` | `a4bc059d5f63` | undetermined | depends on process current directory |
| 69 | Git Extensions | EQ006 | `` GitExtensions.UITests.UserControls.RevisionGrid.CopyContextMenuItemTests::AddHotKey(string,char?) `` | `a989bc241de1` | false positive | P2-073 |
| 70 | Git Extensions | EQ006 | `` Git.hub.Organization::GetHashCode() `` | `f0f34effc09a` | undetermined | real by construction (hash differs per runtime), not executed |
| 71 | Git Extensions | EQ006 | `` GitUITests.Theming.ThemePathProviderInstalledAppTests::UserThemesDirectory_should_be_inside_AppData() `` | `7cb220f5716c` | false positive | P2-073 |
| 72 | Git Extensions | EQ006 | `` GitUITests.Avatars.StaticImageAvatarProviderTests::.ctor() `` | `1c34288f4e74` | false positive | P2-073 |
| 73 | Git Extensions | EQ006 | `` GitExtensions.Plugins.JiraCommitHintPlugin.JiraCommitHintPlugin::QueryHelperLink_Click(object,System.EventArgs) `` | `adf17e2f4e65` | undetermined | real difference by hand trace, not executed: UI handler |
| 74 | Git Extensions | EQ006 | `` GitUI.GitUICommands::GetParameterOrEmptyStringAsDefault(System.Collections.Generic.IReadOnlyList<string>,string) `` | `3c5498270923` | undetermined | no differing ICU/NLS input found; non-public |
| 75 | Git Extensions | EQ006 | `` GitUI.Editor.FileViewer::ProcessApplyOutput(string,byte[],bool) `` | `0e50d2e6a620` | undetermined | real difference by hand trace, not executed: non-public |
| 76 | Git Extensions | EQ006 | `` GitUI.Avatars.StaticImageAvatarProvider::GetCachedResizedImage(int) `` | `6f7cb62ac23a` | undetermined | depends on a native GDI+ failure |
