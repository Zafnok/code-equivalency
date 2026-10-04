# P2-079 IL fragment soundness: gitextensions-8522 with `--il-fallback` (2026-10-03)

Question: after P2-079, is any of the 20 pairs that P1-018's hand check found unproved
(`docs/runs/2026-10-01-il-fallback-verdicts.md`) still Equivalent from IL?

**Answer: none. No pair of the 20 is lowered from IL any more. Ten are Unknown and ten are Equivalent
by congruence of their IOperation bodies, which does not rest on the IL fragment.** The one pair of
the 21 that the hand check found to be a sound proof is still Equivalent from IL, and it is the only
Equivalent the fallback produces in this run.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f.
- equiv: de407df (this ticket's branch; the fix is 6407d3d), mode `full` with `--il-fallback`, no
  `--execute`, default config. One run, started 2026-10-03 22:19.
- Compared with P1-018's `--il-fallback` run at 1d4569a. 58 commits landed on `main` between the
  two, so a difference between the runs is not this ticket's alone. What this run shows is the
  state of the 20 pairs now.
- Load: legacy 48 of 48 C# projects, modern 43 of 43. No project skipped.
- Wall-clock 6,761 s (1h53m), exit 5: one pair-level crash,
  `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()`, which is P2-078 (open).

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.000 | +0.000 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.463 | n/a |
| match | 1 | 0.024 | n/a |
| lower | 13541 | 98.694 | +50.678 |
| verify | 13541 | 4325.441 | -69.052 |
| contracts | 795 | 2297.392 | -400.646 |
| write | 1 | 0.251 | +0.000 |

## The run, next to P1-018's

| | P1-018 (1d4569a) | this run (de407df) |
|---|---|---|
| matched pairs | 13,541 | 13,541 |
| congruent | 12,247 | 12,660 |
| changed pairs | 1,294 | 881 |
| changed pairs without opaque | 585 | 422 |
| pairs the fallback tried (`pairsIlFallbackTried`) | 367 | 353 |
| pairs lowered from IL (`pairsLoweredFromIl`) | 116 | 45 |
| Equivalent from IL | 21 | 1 |

The fallback keeps the IL bodies only when they hold fewer unshared opaques than the IOperation
bodies (ADR 0039). Before the fix a lambda's pointer was a shared opaque from IL and an unshared
one from IOperation, so a pair that held a lambda was kept from IL. Now it is unshared both ways,
and such a pair keeps its IOperation bodies. That, and the lambdas the IOperation lowering has
modelled since P2-067, is why 45 pairs are lowered from IL and not 116. How much of the drop is
each cause was not measured.

The 44 results lowered from IL: Equivalent 1 (`bounded`), Divergent 19 (EQ002 7, EQ006 12),
Unknown(timeout) 8, Unknown(abstraction) 6, Unknown(opaque) 6, Unknown(unaligned-loop) 4. The 45th
is the crashed pair, which has no result.

## The 20 pairs

`lowering` is `operation` for all 20: none is read from IL.

| verdict now | pairs |
|---|---|
| Unknown(opaque), reason `DelegateCreation` on both sides | 8 |
| Unknown(abstraction), on two `delegate:` functions that differ between the sides | 2 |
| Equivalent, `proofMethod: congruence` | 10 |
| Equivalent from IL | 0 |

| procedure | construct (P1-018's hand check) | verdict now |
|---|---|---|
| `CommonTestUtils.GitModuleTestHelper::GetSubmodulesRecursive()` | lambda | Unknown(opaque: `DelegateCreation`) |
| `GitCommands.ExternalLinks.ExternalLinkDefinition::set_NestedSearchPattern(string)` | lambda | Equivalent (congruence) |
| `GitCommands.ExternalLinks.ExternalLinkDefinition::set_RemoteSearchPattern(string)` | lambda | Equivalent (congruence) |
| `GitCommands.ExternalLinks.ExternalLinkDefinition::set_SearchPattern(string)` | lambda | Equivalent (congruence) |
| `GitCommands.ExternalLinks.ExternalLinkDefinition::set_UseRemotesPattern(string)` | lambda | Equivalent (congruence) |
| `GitExtensions.Plugins.GitStatistics.FormGitStatistics::InitializeLinesOfCode()` | lambda and local functions | Unknown(opaque: `DelegateCreation`) |
| `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::RepoObjectTree_moving_first_up_and_last_down_does_nothing()` | lambda | Equivalent (congruence) |
| `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::RepoObjectTree_moving_node_across_hidden_trees_skips_them()` | lambda | Equivalent (congruence) |
| `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_ReorderNodesTest::RepoObjectTree_moving_node_legally_moves_it()` | lambda | Equivalent (congruence) |
| `GitExtensions.UITests.CommandsDialogs.FormBrowse_LeftPanel_SubmodulesTests::RepoObjectTree_should_show_all_submodules()` | `async` lambda | Equivalent (congruence) |
| `GitExtensions.UITests.Script.ScriptRunnerTests::RunScript_with_arguments_with_s_option_with_RevisionGrid_without_selection_shall_display_error_and_return_false()` | lambda | Unknown(abstraction) |
| `GitExtensions.UITests.UserControls.CommitInfo.CommitInfoTests::GetSortedTags_should_throw_on_git_warning()` | lambda | Unknown(abstraction) |
| `GitUI.CommandsDialogs.BrowseDialog.FormUpdates::btnUpdateNow_Click(object,System.EventArgs)` | `async` lambda | Unknown(opaque: `DelegateCreation`) |
| `GitUI.CommandsDialogs.FormBrowse::FillTerminalTab()` | event-handler lambda | Unknown(opaque: `DelegateCreation`) |
| `GitUI.CommandsDialogs.FormFileHistory::LoadFileHistory()` | lambdas and a local function; the local function differs | Equivalent (congruence) |
| `GitUI.CommandsDialogs.FormReflog::Branches_SelectedIndexChanged(object,System.EventArgs)` | `async` local function as a method group | Equivalent (congruence) |
| `GitUI.CommandsDialogs.RepoHosting.ForkAndCloneForm::Init()` | `async` lambda; the lambda differs | Unknown(opaque: `DelegateCreation`) |
| `GitUI.FindAndReplaceForm::btnReplace_Click(object,System.EventArgs)` | `async` lambda | Unknown(opaque: `DelegateCreation`) |
| `GitUI.UserControls.RevisionGrid.Columns.MessageColumnProvider::SortRefs(System.Collections.Generic.IEnumerable<global::GitUIPluginInterfaces.IGitRef>)` | local function as a method group | Unknown(opaque: `DelegateCreation`) |
| `ResourceManager.Xliff.TranslationUtil::AllowTranslateProperty(string)` | method group for `System.Char::IsLetter` | Unknown(opaque: `DelegateCreation`) |

The 21st pair, `GitCommands.Settings.RepoDistSettings::SetValue<T>(string,T,System.Func<T, string>)`,
holds no delegate creation and was the hand check's one sound proof. It is still Equivalent,
`proofMethod: bounded`, `lowering: il`, assuming the callee `GitCommands.SettingsCache::HasValue(string)`.

## The ten that are Equivalent by congruence
Congruence (ADR 0024) compares the two sides' bound fingerprints, which `BoundSerialiser` takes
from the IOperation tree with every lambda's and local function's body in it, and refuses when the
body is runtime-sensitive. It reads the bodies the IL fragment did not, so these ten are not what
P2-079 was about. They were changed pairs at 1d4569a and are congruent now; the run has 413 more
congruent pairs than P1-018's. Which ticket made each of the ten congruent was not traced. For
`FormFileHistory::LoadFileHistory()`, whose local function replaced a `GitExtUtils.Strings`
forwarder with the `System.String` method it calls, P2-068 (ADR 0047) gives both calls one identity.

## Findings
- No pair of the 20 is Equivalent from IL: P2-079's criterion 6 holds.
- The run still exits 5 on one IL-lowered pair: P2-078 (existing).
- Found while writing P2-079's generator, not by this run: the IOperation lowering proves a member
  whose two sides differ only inside a local function it calls by name. P2-127.
