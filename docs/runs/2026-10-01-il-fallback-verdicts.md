# P1-018 IL fallback, decided verdicts: gitextensions-8522 (2026-10-01)

Question: how many changed pairs does `--il-fallback` (ADR 0039) move from Unknown(opaque) to a
decided verdict, and is that at least 5% of changed pairs?

**Answer: 21 of 1,294 changed pairs (1.6%). The fallback stays off by default.** It moves 21 pairs
from Unknown(opaque) to Equivalent and 21 to Divergent. None of those Divergents is reproduced by
replay, so none counts. Even if all 21 counted, the gain would be 42 pairs (3.2%), under the bar
of 65. No pair that is Equivalent without the fallback is Divergent or crashes with it. One pair
lowered from IL crashes the solver (P2-078), and one Divergent becomes Unknown(timeout).

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- equiv: 1d4569a (`main`), mode `full`, all runs at that commit, started 2026-09-30.
- Three runs. The two measured runs are `full` without and with `--il-fallback`. Neither takes
  `--execute`, because replay turns some Unknowns into observed Divergents and that would mix into
  the move being measured. A third run, `--il-fallback --execute`, supplies `properties.replay`.
- The three runs shared one machine and ran concurrently, with starts staggered until the previous
  run had finished loading. Wall-clock times are therefore under shared load, equally for all three.
- Load: legacy 48 of 48 C# projects, modern 43 of 43, in every run. No project skipped.

| | without | with `--il-fallback` | with `--il-fallback --execute` |
|---|---|---|---|
| exit code | 1 | 5 | 5 |
| wall-clock | 29,384 s (8h10m) | 30,182 s (8h23m) | 30,323 s (8h25m) |
| lower phase | 91.6 s | 104.9 s | 103.3 s |
| verify phase | 22,218 s | 22,572 s | 22,446 s |
| after verify, before write (contracts pass, and `execute` 262 s in the third run) | 7,048 s | 7,480 s | 7,743 s |
| pair-level crashes (`properties.unverified`) | 0 | 1 | 1 |

Exit 1 means Divergent results exist. Exit 5 means a pair-level crash (ADR 0023).

## Both runs, all changed pairs
Matched pairs 13,541 and congruent 12,247 in every run, so changed pairs are 1,294 (ADR 0034). The
2026-09-30 full run at bd8e379 had 1,143. The 19 `unmatched-overload` results are not matched
pairs and are left out.

| | without | with |
|---|---|---|
| changed pairs | 1,294 | 1,294 |
| pairs the fallback tried (`pairsIlFallbackTried`) | n/a | 367 |
| pairs lowered from IL (`pairsLoweredFromIl`) | n/a | 116 |
| changed pairs without opaque | 522 (40.3%) | 585 (45.2%) |
| Equivalent | 83 | 104 |
| Divergent (EQ002 + EQ006) | 423 (83 + 340) | 449 (88 + 361) |
| Unknown(abstraction) | 278 | 271 |
| Unknown(timeout) | 224 | 233 |
| Unknown(opaque) | 213 | 156 |
| Unknown(unaligned-loop) | 69 | 76 |
| Unknown(recursion) | 4 | 4 |
| no result (crash) | 0 | 1 |

Of the 251 pairs the fallback tried and left on the IOperation lowering, 30 hold a compiler state
machine (`il-state-machine`: iterators and `async`), which the IL lowering refuses. For the other
221 the IL lowering held no fewer unshared opaques.

## The 116 pairs lowered from IL: verdict before and after

| | before (without) | after (with) |
|---|---|---|
| Equivalent | 0 | 21 |
| Divergent | 16 | 42 |
| Unknown(opaque) | 73 | 16 |
| Unknown(timeout) | 16 | 25 |
| Unknown(abstraction) | 9 | 3 |
| Unknown(unaligned-loop) | 2 | 8 |
| no result (crash) | 0 | 1 |

| before | after | pairs |
|---|---|---|
| Unknown(opaque) | Equivalent | 21 |
| Unknown(opaque) | Divergent | 21 |
| Unknown(opaque) | Unknown(opaque) | 13 |
| Unknown(opaque) | Unknown(timeout) | 11 |
| Unknown(opaque) | Unknown(unaligned-loop) | 6 |
| Unknown(opaque) | Unknown(abstraction) | 1 |
| Divergent | Divergent | 15 |
| Divergent | Unknown(timeout) | 1 |
| Unknown(timeout) | Unknown(timeout) | 11 |
| Unknown(timeout) | Divergent | 2 |
| Unknown(timeout) | Unknown(opaque) | 2 |
| Unknown(timeout) | no result (crash) | 1 |
| Unknown(abstraction) | Divergent | 4 |
| Unknown(abstraction) | Unknown(abstraction) | 2 |
| Unknown(abstraction) | Unknown(timeout) | 2 |
| Unknown(abstraction) | Unknown(opaque) | 1 |
| Unknown(unaligned-loop) | Unknown(unaligned-loop) | 2 |

All 21 new Equivalents are `proofMethod: bounded`. One carries an unproven callee assumption.

What still holds the 52 IL-lowered pairs that end Unknown:
- 25 time out.
- 16 keep an opaque. By ILAst key (pairs holding it): `LdLoc` 6, `CallVirt` 4,
  `LdLoc[caught exception]` 3, `LdObj` 2, `NewArr` 1, `DefaultValue` 1, `Conv` 1.
- 8 are unaligned loops.
- 3 are abstractions: `System.String::op_Equality`, `System.DateTime::op_Subtraction` with
  `conv.f64.i64`, and one shared opaque fragment.

`Nullable<T>` getters and `string.Format` do not appear among the abstractions, so this run gives no
case for catalogue entries. Whether they are behind the 25 timeouts cannot be read from the SARIF.

## Criterion 2: Divergents only the IL run produces
28 pairs are Divergent with the fallback and not without it. 27 were lowered from IL. The 28th kept
the IOperation lowering and moved from Unknown(timeout), which is wall-clock noise (see below). The
third run gives the same rule id for all 28. **Reproduced: 0. `not-reproduced`: 0.** So no P2 ticket
follows from replay, and no Divergent counts towards the gain.

| replay | reason | pairs |
|---|---|---|
| `not-constructible` | the divergence is in the call trace, which replay does not observe | 12 |
| `not-constructible` | not public | 10 |
| `not-constructible` | the model constrains a heap map, cast or field | 4 |
| `not-constructible` | no argument can be built | 1 |
| `not-applicable` | | 1 |

| procedure | before | rule | lowering | `properties.replay` |
|---|---|---|---|---|
| `BugReporter.Serialization.SerializableException::FromXmlString(string)` | Unknown(abstraction) | EQ006 | il | not-constructible (heap map, cast or field) |
| `GitCommands.DiffMergeTools.DiffMergeToolConfigurationManager::GetToolSetting(string,GitCommands.DiffMergeTools.DiffMergeToolType,string)` | Unknown(opaque) | EQ002 | il | not-constructible (not public) |
| `GitCommands.Git.GitItemStatusNameEqualityComparer::GetHashCode(GitCommands.GitItemStatus)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitCommands.GitModule::GetFetchArgs(string,string,string,bool?,bool,bool,bool)` | Unknown(timeout) | EQ002 | il | not-constructible (call trace) |
| `GitCommands.GitPushAction::ToString()` | Unknown(abstraction) | EQ002 | il | not-constructible (call trace) |
| `GitCommands.PathUtil::ResolveRelativePath(string,string)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitCommands.RevisionReader::ParseCommitBody(GitCommands.RevisionReader.StringLineReader,string)` | Unknown(timeout) | EQ006 | il | not-constructible (not public) |
| `GitExtensions.Plugins.GitHub3.GitHub3Plugin::OpenLink(string)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitExtensions.UITests.Script.ScriptRunnerTests::Setup()` | Unknown(opaque) | EQ002 | il | not-constructible (heap map, cast or field) |
| `GitUI.Avatars.GravatarProvider::GetAvatarAsync(string,string,int)` | Unknown(abstraction) | EQ006 | il | not-constructible (call trace) |
| `GitUI.Avatars.InitialsAvatarProvider::GetInitialsAndHashCode(string,string)` | Unknown(opaque) | EQ006 | il | not-constructible (not public) |
| `GitUI.CommandsDialogs.FormReflog::.ctor()` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitUI.CommandsDialogs.SettingsDialog.SettingsTreeViewUserControl::GotoPage(GitUI.CommandsDialogs.SettingsDialog.SettingsPageReference)` | Unknown(opaque) | EQ006 | il | not-constructible (heap map, cast or field) |
| `GitUI.FileStatusList::FormatListViewItem(System.Windows.Forms.ListViewItem,GitUI.PathFormatter,int)` | Unknown(opaque) | EQ006 | il | not-constructible (not public) |
| `GitUI.FindAndReplaceForm::UpdateTitleBar()` | Unknown(opaque) | EQ006 | il | not-constructible (not public) |
| `GitUI.Shells.ConEmuControlExtensions::ChangeFolder(ConEmu.WinForms.ConEmuControl,GitUI.Shells.IShellDescriptor,string)` | Unknown(opaque) | EQ002 | il | not-constructible (call trace) |
| `GitUI.Theming.ComboBoxRenderer::RenderBorder(GitUI.Theming.ThemeRenderer.Context,int,System.Drawing.Rectangle)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitUI.Theming.ComboBoxRenderer::RenderReadonlyDropDown(GitUI.Theming.ThemeRenderer.Context,int,System.Drawing.Rectangle)` | Unknown(opaque) | EQ006 | il | not-constructible (not public) |
| `GitUI.Theming.EditRenderer::RenderEditBorderNoScroll(GitUI.Theming.ThemeRenderer.Context,int,System.Drawing.Rectangle)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitUI.Theming.EditRenderer::RenderEditText(GitUI.Theming.ThemeRenderer.Context,int,System.Drawing.Rectangle)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitUI.Theming.HeaderRenderer::RenderBackground(System.IntPtr,int,int,System.Drawing.Rectangle,System.NativeMethods.RECTCLS)` | Unknown(opaque) | EQ006 | il | not-constructible (not public) |
| `GitUI.Theming.ThemeLoader::StyleRuleThemeException(ExCSS.StyleRule,string)` | Unknown(opaque) | EQ002 | il | not-constructible (not public) |
| `GitUI.Theming.TooltipRenderer::RenderBackground(System.IntPtr,int,int,System.Drawing.Rectangle,System.NativeMethods.RECTCLS)` | Unknown(opaque) | EQ006 | il | not-constructible (call trace) |
| `GitUI.UserControls.RevisionGrid.Graph.BranchFinder::ParseMergeMessage(string,bool)` | Unknown(abstraction) | EQ006 | il | not-constructible (not public) |
| `GitUIPluginInterfaces.BuildServerIntegration.BuildServerSettingsHelper::IsUrlValid(string)` | Unknown(opaque) | EQ006 | il | not-applicable |
| `GitUIPluginInterfaces.CredentialsManager.AdysTechCredentialManagerWrapper::RemoveCredentials(string)` | Unknown(opaque) | EQ002 | il | not-constructible (not public) |
| `ICSharpCode.TextEditor.Actions.ToggleBlockComment::FindSelectedCommentRegion(ICSharpCode.TextEditor.Document.IDocument,string,string,int,int)` | Unknown(timeout) | EQ006 | operation | not-constructible (no argument can be built) |
| `NetSpell.SpellChecker.Dictionary.Affix.AffixUtility::RemoveSuffix(string,NetSpell.SpellChecker.Dictionary.Affix.AffixEntry)` | Unknown(opaque) | EQ006 | il | not-constructible (heap map, cast or field) |

The 20 EQ006 results on IL-lowered pairs cite these `runtime-changes.json` rows: GDI+ failures
surface as `ExternalException` 7, `String.GetHashCode` 2, URI length limits 2, ICU culture-sensitive
overloads 2, regex ranges 2, and one each for `XmlSerializer` obsolete properties,
`UseShellExecute`, `UriBuilder` setters, `ArgumentNullException` and `Path.GetFileName`. P2-047's
audit put Divergent precision at 3.8%, and its tickets P2-073 to P2-075 own rows that match on the
member alone. The IL lowering exposes the calls those rows fire on. It does not change the rows.

## Criterion 3: no Equivalent regresses
**Holds.** Of the 12,330 results that are Equivalent without the fallback, none is Divergent and none
is unverified with it.

Two regressions fall outside criterion 3's wording and are findings:
- A crash. `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()` is Unknown(timeout)
  without the fallback. With it, the pair is lowered from IL and the encoder throws a `Z3Exception`
  ("domain sort System.Drawing.PointF and parameter System.Drawing.Rectangle do not match", from
  `FragmentEncoder.EncodeInstruction` at `MkSelect`). The pair gets no result and the run exits 5.
  P1-017's IL mode of the differential gate did not produce this shape. Filed as P2-078.
- A decided verdict lost. `JenkinsIntegration.JenkinsAdapter::FormatToGetJson(string,bool)` is
  Divergent (EQ006) without the fallback and Unknown(timeout) with it. ADR 0039's rule compares
  unshared opaques only, so it cannot see that the IL bodies are harder for the solver. Recorded in
  P2-078's Notes for whichever ticket next proposes turning the default on.

## Criterion 4: the gain
| | pairs | share of 1,294 |
|---|---|---|
| Unknown(opaque) to Equivalent | 21 | 1.6% |
| Unknown(opaque) to Divergent, reproduced by replay | 0 | 0.0% |
| **gain** | **21** | **1.6%** |
| bar (ADR 0028, ADR 0039) | 65 | 5.0% |
| for comparison: every Unknown(opaque) to Divergent counted, reproduced or not | 42 | 3.2% |
| for comparison: every move from any Unknown reason to a decided verdict | 48 | 3.7% |

The gain is under 5%, so the default stays off and `docs/ROADMAP.md`'s Post-MVP list records the
numbers. The spike's lowerability estimate held: changed pairs without opaque rise by 63 (4.9% of
changed pairs). What ADR 0039 warned of also held: lowerable is not proved. Of the 73 IL-lowered
pairs that were Unknown(opaque), 31 are still Unknown.

## Noise between the two measured runs
Seven pairs the fallback did not lower changed verdict between the runs. Six moved to or from a
timeout: Unknown(abstraction) to Unknown(timeout) 2, and one each of Unknown(timeout) to
Unknown(abstraction), Unknown(timeout) to Divergent, Unknown(timeout) to Unknown(unaligned-loop) and
Unknown(unaligned-loop) to Unknown(timeout). The seventh moved from Divergent to
Unknown(unaligned-loop). This fits a wall-clock budget under shared load (P2-050), but no pair's
cause was checked. None is counted above.

## Findings
- The gain is 1.6%, under the 5% bar: the fallback stays off by default (this ticket).
- An IL-lowered pair crashes the encoder with a sort mismatch, and one IL-lowered Divergent becomes
  Unknown(timeout): P2-078.
- Six pairs that end Unknown take 5.4 of the 6.8 verify hours, and about two more hours follow in a
  pass that no phase logs: P2-076, P2-077 (filed during this run from the 2026-09-29 log; this
  run's phase times repeat the pattern).
- Seven pairs the fallback did not lower change verdict between two runs of one commit, six of them
  to or from a timeout: P2-050 (existing).
- No `not-reproduced` replay, so no ticket from criterion 2.
