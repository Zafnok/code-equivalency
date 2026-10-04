# P1-021 failure refinement: what it answers, and what it would answer on `timeout` Unknowns (2026-10-04)

Questions: how often do ADR 0037's two queries (`newFailures`, `removedFailures`; ticket P1-013)
answer `none-proved` or `found` on real Unknowns, and what would they answer on a `timeout` Unknown,
where ADR 0037 does not ask?

**Answers.**
- **Today: 11 of the 1,072 queried Unknowns (1.0%) have a `none-proved` `newFailures`.** 30 (2.8%)
  have a `found` one and 1,031 (96.2%) are `unknown`. The queries take 7% to 13% of a run's verify
  time.
- **On `timeout` Unknowns: 2 of the 138 that are still a `timeout` at this commit (1.4%) would have a
  `none-proved` `newFailures`.** That is below ADR 0028's 5%. 17 of the 138 (12.3%) would get an
  answer from at least one of the two queries, most of them `found`. A second run gave 2 of 140 and
  19 of 140 (13.6%).
- **ADR 0037's two reasons for not asking are half right.** "Seldom finishes": the solver gives up on
  `newFailures` for 69 of the 138 (50%), and decides the other half. "Would triple the cost": the two
  queries add 0.44 times the pair's time in both runs, not twice it.

Criterion 4 of the ticket files a follow-up at 5% `none-proved` and the measured share is 1.4%. The
follow-up, P1-035, is filed regardless, on the user's direction of 2026-10-04; the ticket's Notes
record the deviation.

## Setup
- Runs read (criterion 1), each a `full` run at the default config (`resourceLimit` 5,000,000,
  `timeoutMs` 60,000, bound 3):

  | Pair | Run | equiv | EQ003 results | `verify` phase |
  |---|---|---|---|---|
  | gitextensions-8522 | P2-076's "after, again" run (`docs/runs/2026-10-02-pair-time.md`) | `7cbe2d7` | 608 | 4,995 s |
  | gitextensions-9860 | `docs/runs/2026-10-03-full-gitextensions-9860` | `8e0ed3c` | 441 | 3,448 s |
  | jellyfin-13023 | `docs/runs/2026-10-03-full-jellyfin-13023` | `8e0ed3c` | 525 | 4,858 s |

  Together they hold the 400 `timeout` Unknowns the ticket names (164, 98 and 138).
- Pairs asked (criterion 2): the 164 `timeout` Unknowns of the gitextensions-8522 run. Pair: human,
  gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f.
- equiv for criterion 2: `main` at `a74f2e0`, win-x64, both solutions loaded through the production
  frontend with the default config, as P1-019's spike does. All 164 identities still match a pair.
- Tool: `tools/spikes/failure-refinement/` (see its README for the commands). `--self-test` passes:
  `a + 1` against `a + 2` is `none-proved` for both queries, and a body that returns against one
  that throws when its argument is 0 is `found` for `newFailures`.
- What criterion 2 ran, per pair: `Z3Backend.Verify` once (the baseline, the query `compare` asks),
  then the production `FailureRefinementQuery`, unchanged. A query's solver time and whether a check
  gave up are read from the `check:` lines it writes to the run log. A query that is `unknown`
  because a check gave up is listed as `timeout`.
- Machine: one Windows box, 24 logical cores, six threads, shared with three other sessions' spikes
  throughout. Two runs: 899 s and 1,123 s wall-clock after a 113 s load.

## What the queries answer today (criterion 1)
`not queried` is an Unknown with no `failureRefinement`: every `timeout`, and every
`unmatched-overload`, which is not a matched pair. No run has an `unbound` Unknown.

All three runs, 1,574 Unknowns, 1,072 queried:

| `newFailures` by `unknownReason` | Unknowns | none-proved | found | unknown | not queried |
|---|---|---|---|---|---|
| opaque | 734 | 0 | 2 | 732 | 0 |
| timeout | 400 | 0 | 0 | 0 | 400 |
| abstraction | 246 | 11 | 6 | 229 | 0 |
| unmatched-overload | 102 | 0 | 0 | 0 | 102 |
| unaligned-loop | 89 | 0 | 22 | 67 | 0 |
| recursion | 3 | 0 | 0 | 3 | 0 |
| **all** | 1,574 | 11 | 30 | 1,031 | 502 |

| `removedFailures` by `unknownReason` | Unknowns | none-proved | found | unknown | not queried |
|---|---|---|---|---|---|
| opaque | 734 | 0 | 2 | 732 | 0 |
| timeout | 400 | 0 | 0 | 0 | 400 |
| abstraction | 246 | 5 | 8 | 233 | 0 |
| unmatched-overload | 102 | 0 | 0 | 0 | 102 |
| unaligned-loop | 89 | 0 | 20 | 69 | 0 |
| recursion | 3 | 0 | 0 | 3 | 0 |
| **all** | 1,574 | 5 | 30 | 1,037 | 502 |

Per run, the queried Unknowns only (`newFailures` / `removedFailures`):

| Pair | Reason | Queried | none-proved | found | unknown |
|---|---|---|---|---|---|
| gitextensions-8522 | opaque | 238 | 0 / 0 | 1 / 1 | 237 / 237 |
| gitextensions-8522 | abstraction | 142 | 10 / 4 | 2 / 4 | 130 / 134 |
| gitextensions-8522 | unaligned-loop | 44 | 0 / 0 | 15 / 14 | 29 / 30 |
| gitextensions-8522 | recursion | 1 | 0 / 0 | 0 / 0 | 1 / 1 |
| gitextensions-9860 | opaque | 253 | 0 / 0 | 0 / 0 | 253 / 253 |
| gitextensions-9860 | abstraction | 52 | 1 / 1 | 0 / 0 | 51 / 51 |
| gitextensions-9860 | unaligned-loop | 16 | 0 / 0 | 2 / 2 | 14 / 14 |
| gitextensions-9860 | recursion | 2 | 0 / 0 | 0 / 0 | 2 / 2 |
| jellyfin-13023 | opaque | 243 | 0 / 0 | 1 / 1 | 242 / 242 |
| jellyfin-13023 | abstraction | 52 | 0 / 0 | 4 / 4 | 48 / 48 |
| jellyfin-13023 | unaligned-loop | 29 | 0 / 0 | 5 / 4 | 24 / 25 |

- Every `none-proved` is on an `abstraction` Unknown, and 10 of the 11 `newFailures` ones are on
  gitextensions-8522. 5 of the 11 are `none-proved` in both directions.
- An `opaque` Unknown is `unknown` in 732 of 734 cases. ADR 0037 expected the queries to pay off
  "where the guards run before the first unshared opaque node". On these runs they almost never do.
- A `found` is most common on `unaligned-loop` (22 of 89, 24.7%).

Time, from each run's `loweringCensus.failureRefinement`:

| Pair | Pairs queried | Failure-refinement time | `verify` phase | Share |
|---|---|---|---|---|
| gitextensions-8522 | 425 | 429 s | 4,995 s | 8.6% |
| gitextensions-9860 | 323 | 435 s | 3,448 s | 12.6% |
| jellyfin-13023 | 324 | 359 s | 4,858 s | 7.4% |

## What they would answer on a `timeout` Unknown (criterion 2)
First run, with the second in brackets where it differs.

The baseline: `main` has moved since the run, so not every pair is still a `timeout`.

| Baseline at `a74f2e0` | Pairs |
|---|---|
| timeout | 138 (140) |
| Equivalent | 16 |
| Unknown, another reason (abstraction, opaque, unaligned-loop) | 8 (6) |
| Divergent | 2 |

The two queries, over all 164:

| Query | Pairs | none-proved | found | unknown | timeout | Solver seconds: sum / median / p90 / max |
|---|---|---|---|---|---|---|
| `newFailures` | 164 | 17 | 17 | 59 | 71 | 521 / 1.1 / 7.8 / 38.0 (682 / 1.5 / 10.5 / 50.5) |
| `removedFailures` | 164 | 18 | 15 | 58 (57) | 73 (74) | 607 / 1.0 / 7.3 / 51.4 (786 / 1.6 / 11.5 / 53.9) |

By baseline. Only the first row is about pairs that would be `timeout` Unknowns today:

| Baseline | Query | Pairs | none-proved | found | unknown | timeout |
|---|---|---|---|---|---|---|
| timeout | `newFailures` | 138 (140) | 2 | 14 (15) | 53 | 69 (70) |
| timeout | `removedFailures` | 138 (140) | 3 | 14 | 50 | 71 (73) |
| Equivalent | `newFailures` | 16 | 15 | 0 | 1 | 0 |
| Equivalent | `removedFailures` | 16 | 15 | 0 | 1 | 0 |
| Unknown, another reason | `newFailures` | 8 (6) | 0 | 1 (0) | 5 | 2 (1) |
| Unknown, another reason | `removedFailures` | 8 (6) | 0 | 0 | 6 (5) | 2 (1) |
| Divergent | `newFailures` | 2 | 0 | 2 | 0 | 0 |
| Divergent | `removedFailures` | 2 | 0 | 1 | 1 | 0 |

- Over all 164, 17 (10.4%) are `none-proved` for `newFailures`. 15 of those are pairs `main` now
  proves Equivalent, which need no weaker claim. The share that matters is over the pairs still a
  `timeout`: 2 of 138 (1.4%).
- The two directions agree: of the 138, 14 are `found` both ways and 2 `none-proved` both ways. Of
  the 14 `found` `newFailures`, 10 are methods of the two test projects.
- A `found` on a pair whose full query timed out is an untainted model on which one side returns and
  the other throws. The full query asks for any differing observable and did not find it within the
  same budget.
- The 53 `unknown` are a model whose replay is tainted, or a failure possible only through an opaque
  node or past the bound. The spike does not tell these apart.

Cost on the pairs still a `timeout`:

| | First run | Second run |
|---|---|---|
| Baseline pair seconds (sum) | 2,811 | 3,719 |
| Added by the two queries (sum) | 1,238 | 1,633 |
| Added / baseline | 0.44 | 0.44 |

Over all 164 the two queries add 1,262 s (1,656 s), which is 25% (33%) of the run's 4,995 s `verify`
phase. These ran on six threads on a shared machine, so the ratio to the baseline is the steadier
figure.

### Repeatability
160 of 164 pairs gave the same baseline and the same two outcomes in both runs. The four that differ
each sit on a query the resource limit ends in one run and not in the other (P2-100): two baselines
moved between `timeout` and another Unknown reason, and two `removedFailures` answers moved between
`timeout` and `found`, and between `unknown` and `found`.

### The pairs still a `timeout` that get an answer (first run)

| Procedure | `newFailures` | `removedFailures` |
|---|---|---|
| `GitCommandsTests.CommitMessageManagerTests::AmendState_should_be_false_if_file_contains(string)` | found | found |
| `GitCommandsTests.CommitMessageManagerTests::AmendState_should_be_true_if_file_contains(string)` | found | found |
| `GitCommandsTests.CommitTemplateManagerTests::LoadGitCommitTemplate_should_load_file_content()` | found | found |
| `GitCommandsTests.FileAssociatedIconProviderTests::Get_should_add_entry_for_extension_once()` | found | found |
| `GitCommandsTests.Git.GitDirectoryResolverTests::Resolve_submodule_real_filesystem()` | found | found |
| `GitCommandsTests.GitRevisionInfoProviderTests::LoadChildren_should_return_shallow_tree_for_GitItem_with_updated_FileName()` | found | found |
| `GitCommandsTests.Helpers.PathUtilTest::DeleteWithExtremePrejudice_should_return_true_if_delete_successful()` | found | found |
| `GitCommandsTests.Remote.ConfigFileRemoteSettingsManagerTests::CreateSubstituteRef(string,string,string)` | found | found |
| `GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent()` | found | found |
| `GitUITests.Avatars.AvatarPersistentCacheTests::ClearCacheAsync_should_remove_all()` | found | found |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.ChecklistSettingsPage::ShellExtensionsRegistered_Click(object,System.EventArgs)` | found | found |
| `ICSharpCode.TextEditor.Util.TipSection::SetRequiredSize(System.Drawing.SizeF)` | found | found |
| `NetSpell.SpellChecker.Dictionary.WordDictionary::SaveUserFile()` | found | found |
| `TranslationApp.Program::Main()` | found | found (timeout) |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.FormFixHome::LoadSettings()` | none-proved | none-proved |
| `GitUI.UserControls.RevisionGrid.IndexWatcher::SetFileSystemWatcher()` | none-proved | none-proved |
| `GitUI.CommitInfo.CommitInfo::.ctor()` | unknown | none-proved |

The second run adds `GitCommands.EnvironmentConfiguration::SetEnvironmentVariables()` (`found`,
`unknown`; its baseline was Unknown(abstraction) in the first run) and
`GitUI.CommandsDialogs.WorktreeDialog.FormManageWorktree::Worktrees_CellClick(object,System.Windows.Forms.DataGridViewCellEventArgs)`
(`unknown`, `found`).

## The two lines criterion 3 asks for
- Share of today's queried Unknowns with a `none-proved` `newFailures`: **11 of 1,072 (1.0%)**.
- Share of the `timeout` Unknowns that would have one: **2 of 138 (1.4%)** of those still a
  `timeout` at `a74f2e0`; 17 of 164 (10.4%) of the run's, 15 of them now proved Equivalent.

## Findings
- Asking on a `timeout` Unknown answers 17 to 19 pairs of about 140 at 0.44 times their verify time:
  P1-035.
- 16 of the run's 164 `timeout` Unknowns are Equivalent at `a74f2e0`, and 2 Divergent. Nothing to
  file: this is what the tickets merged since `7cbe2d7` did.
- 14 pairs are `found` in both directions where the full query times out. Whether these are
  divergences the full query should report is not measured here; P1-035's Out of scope points at
  P1-020 and P1-031.
- Not measured: gitextensions-9860 and jellyfin-13023's 236 `timeout` Unknowns. The ticket asks for
  gitextensions-8522 only.
