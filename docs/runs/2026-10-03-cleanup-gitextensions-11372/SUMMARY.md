# cleanup run: gitextensions-11372

- Pair: cleanup, gitextensions/gitextensions PR #11372, legacy 030255ef17cb, modern 1cfb0e4441e2
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11372, IDE0028 collection expressions; no runtime change)
- equiv: 465133d (P2-099's branch), mode full, wall-clock 1838s (31m), exit 5 (one pair crashed in
  lowering; no load failure). The box was shared with another session's two corpus runs.
- `--execute`: not run. Both sides run on net8.0, and this box has Microsoft.NETCore.App 6.0.36,
  10.0.9 and 10.0.12 only (ADR 0040 decision 3).
- This is P2-099's rerun of `docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md`, which ran
  equiv 46e6636. "Before" below is that run.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.001 | +0.001 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.583 | n/a |
| match | 1 | 0.028 | n/a |
| lower | 14560 | 83.307 | +37.348 |
| verify | 14559 | 1713.122 | -121.442 |
| contracts | 214 | 0.004 | +0.002 |
| write | 1 | 0.316 | +0.000 |

Verify took 16,830 seconds before. The slowest pair this time is
`GitUI.CommandsDialogs.FormCommit::Stage(System.Collections.Generic.IReadOnlyList<global::GitCommands.GitItemStatus>)`
at 67 seconds; the two pairs that ran 91 and 48 minutes before no longer do. P2-076 landed between
the two runs.

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes: net8.0 on both sides, as before. No pair crosses a runtime.
- Both sides were restored with `dotnet restore --force`. The Build Tools `MSBuild -t:restore` the
  skill names for a legacy side fails on this SDK-style net8.0 solution with MSB4018.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14560 | 14560 |
| analysed lines | 195109 | 195131 |

- Matched pairs 14560; without opaque 10931 (75.1%, before 68.7%); whole-body opaque 83 (0.6%); congruent 14208 (97.6%)
- Unchanged share: 97.6% (`pairsCongruent`); the file-level proxy gives 70.6% of lines (1511 of 1707 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.6%. Not the row above; ADR 0034.

Top opaque reasons (up to 15):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| switch-pattern | 1246 | 1246 | P1-014, P1-015, P1-016 (ADR 0039) |
| Binary | 682 | 682 | P2-087 |
| Conversion | 650 | 651 | P1-014 (ADR 0039) |
| DefaultValue | 501 | 501 | none |
| InstanceReference | 452 | 452 | none |
| DelegateCreation | 252 | 252 | none |
| CaughtException | 132 | 132 | none |
| ImplicitIndexerReference | 121 | 121 | none |
| CollectionExpression | 0 | 104 | P2-109 |
| InterpolatedString | 99 | 99 | P1-014 (ADR 0039) |
| DeconstructionAssignment | 79 | 79 | P1-014 (ADR 0039) |
| iterator | 77 | 77 | none |
| ArrayElementReference | 67 | 67 | none |
| CompoundAssignment | 61 | 61 | P1-014 (ADR 0039) |
| ref-argument | 61 | 61 | none |

`Conversion` was 2151 and 2174. The legacy side has no collection expression, so its fall of 1501
is target-typed `new()`, which P2-099 lowers as the creation it spells. The modern side falls by
1523.

## Changed code
- Changed pairs 351 of 14560; without opaque 133 (before 23); whole-body opaque 8
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 37.9% (before 6.6%)

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 133 | n/a |
| CollectionExpression | 53 | P2-109 |
| Conversion | 22 | P1-014 |
| switch-pattern | 16 | P1-014 |
| CollectionExpression+switch-pattern | 15 | P2-109, P1-014 |
| no-body | 6 | M4-008 (backlog) |
| Binary | 6 | P2-087 |
| CaughtException | 5 | none |
| DelegateCreation | 5 | none |
| CollectionExpression+Conversion | 5 | P2-109 |
| ImplicitIndexerReference | 3 | none |
| Conversion+DefaultValue+switch-pattern | 3 | P1-014 |
| DelegateCreation+switch-pattern | 3 | P1-014 |
| DefaultValue | 3 | none |
| ArrayCreation | 3 | none |

`CollectionExpression` is in the reason set of 104 changed pairs. Before, `Conversion` was in the
reason set of 308.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (unchanged from the run before;
  `-Packages` not run again)

## Verdicts
- By rule: EQ001 14368, EQ002 28, EQ003 186, EQ004 0, EQ005 0, EQ006 0. One pair has no result: it
  crashed in lowering and is listed in `run.properties.unverified`.
- By proofMethod: congruence 14208, bounded 116, lockstep-induction 44
- Unknown by scope: line 59, method 127 (104 over matched pairs plus 23 `unmatched-overload`).
  Line-scoped Unknown share: 31.7%
- Top Unknown reasons: opaque 114 (line 59, method 55), timeout 39, unmatched-overload 23,
  abstraction 5, unaligned-loop 4, recursion 1
- `unbound` Unknowns: 0
- Top abstractions: `delegate` 6, `op:Newtonsoft.Json.Linq.JToken::op_Implicit(string)` 2,
  `opaque Conversion` 2
- Review list: 31 groups for 214 flagged results; flagged results as a share of matched pairs: 1.5%.
  Top five: `EQ003 opaque:CollectionExpression: 68`, `EQ003 timeout: 39`,
  `EQ003 unmatched-overload: 23`, `EQ002 proofMethod:none: 17`, `EQ003 opaque:DelegateCreation: 7`.

The 351 changed pairs, by verdict:

| | before (46e6636) | this run |
|---|---|---|
| Proved Equivalent by the solver | 7 (2.0%), all `bounded` | 160 (45.6%): `bounded` 116, `lockstep-induction` 44 |
| Divergent | 25 (7.1%) | 28 (8.0%) |
| Unknown | 319 (90.9%) | 163 (46.4%): opaque 114, timeout 39, abstraction 5, unaligned-loop 4, recursion 1 |

The 23 `unmatched-overload` Unknowns are not matched pairs and are left out.

## Divergent, adjudicated (P2-047's method)
No replay exists, so each was hand-traced as before: the two files were compared, and the first call
at which the model's two traces part was read against both bodies.

| Cause | EQ002 | Classification |
|---|---|---|
| A caller-file-path argument differs because the two sides are two checkout directories (P2-098) | 24 | false positive |
| A build-generated commit constant differs between any two commits (P2-098) | 1 | real, by construction; not a behaviour change of the pull request |
| A collection expression's elements are evaluated ahead of it, so its calls are in another order than the initializer's (P2-109) | 3 | false positive |

Confirmed behaviour changes 0, false positive 27, real by construction 1, undetermined 0.

- **Caller file path, 24.** The 22 of the run before, and two that were Unknown then and lower
  fully now: `GitCommandsTests.GitModuleTests::GetDiffChangedFilesFromString(string,GitCommands.StagedStatus,string)`
  and `GitExtensions.UITests.ScriptEngine.ScriptManagerTests::Can_save_settings()`. Both call the
  snapshot-verification method, and the second is in a file that is byte-identical on both sides.
- **Commit constant, 1.** `BugReporter.Program::Main()`, as before.
- **Collection expression, 3.** The two the run before named are still EQ002:
  `GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Serialize_recent_repositories()`
  and
  `GitCommandsTests.UserRepositoryHistory.Legacy.RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`.
  Each element there has an object initializer, so the control flow graph evaluates every element
  into a flow capture before the collection expression. The modern trace is every element's
  constructor, then the list's constructor and its `Add` calls; the legacy one is the list's
  constructor, then each element followed by its `Add`. The same calls, in two orders. Both pairs
  also call the snapshot method, so they would stay flagged for the first cause. The third is new:
  `GitCommandsTests.ExternalLinks.ExternalLinkRevisionParserTests::GetDefaultRemotes()` was Unknown
  before. Its legacy side is a target-typed `new()` and three `Add` calls, which now lower; its
  modern side builds a `BindingList<T>`, which stays opaque, after its elements.

**The cleanup changed no behaviour that this run found.**

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- P2-099 moves the solver-proved share of changed pairs from 2.0% to 45.6%, and the lowerable
  share from 6.6% to 37.9%.
- The two EQ002 P2-099 names are still EQ002, and one more of the same kind appeared: elements
  evaluated ahead of the collection expression. `CollectionExpression` is opaque in 104 changed
  pairs (targets P2-099 left out, and spreads): P2-109.
- 24 false EQ002 and 1 by-construction EQ002 from build-location constants: P2-098, open.
- The lowering crash on `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`
  is unchanged, and the run exits 5: P2-105.
- 39 timeouts: P2-101, open.
- `MSBuild -t:restore` fails on an SDK-style net8.0 legacy side; `dotnet restore` works. A skill
  wording fix, not a ticket.
