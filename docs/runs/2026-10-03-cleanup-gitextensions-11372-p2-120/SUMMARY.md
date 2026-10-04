# cleanup run: gitextensions-11372

- Pair: cleanup, gitextensions/gitextensions PR #11372, legacy 030255ef17cb, modern 1cfb0e4441e2
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11372, IDE0028 collection expressions; no runtime change)
- equiv: 259d051 (P2-120's branch), mode full, wall-clock 1325s (22m), exit 5 (one pair crashed in
  lowering; no load failure)
- `--execute`: not run. Both sides run on net8.0, which this box does not have (ADR 0040 decision 3).
- This is P2-120's rerun of `docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md`, which ran
  equiv 465133d. "Before" below is that run. `main` moved between the two (ADR 0043, ADR 0046 and
  others), so not every difference is P2-120's: a `--lower-only` run of `main` at 2c2f682, before
  this ticket's change, already had 14240 congruent pairs.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.000 | +0.000 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.499 | n/a |
| match | 1 | 0.022 | n/a |
| lower | 14560 | 105.555 | +43.895 |
| verify | 14559 | 1173.111 | -99.006 |
| contracts | 132 | 9.669 | -8.994 |
| write | 1 | 0.276 | +0.000 |

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes: net8.0 on both sides. No pair crosses a runtime.
- Both sides were restored with `dotnet restore --force`.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14560 | 14560 |
| analysed lines | 195113 | 195135 |

- Matched pairs 14560; without opaque 10984 (75.4%, before 75.1%); whole-body opaque 83 (0.6%); congruent 14240 (97.8%)
- Unchanged share: 97.8% (`pairsCongruent`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15):

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| switch-pattern | 1246 | 1246 | P2-122 |
| Binary | 682 | 682 | P2-087 |
| Conversion | 650 | 651 | P2-123 |
| DefaultValue | 505 | 505 | none |
| InstanceReference | 452 | 452 | none |
| DelegateCreation | 253 | 253 | none |
| CaughtException | 132 | 132 | none |
| ImplicitIndexerReference | 121 | 121 | none |
| InterpolatedString | 99 | 99 | P1-014 (ADR 0039) |
| DeconstructionAssignment | 79 | 79 | P1-014 (ADR 0039) |
| iterator | 77 | 77 | none |
| ArrayElementReference | 67 | 67 | none |
| CompoundAssignment | 61 | 61 | P1-014 (ADR 0039) |
| ref-argument | 61 | 61 | none |
| Tuple | 51 | 51 | none |

`CollectionExpression` is 0 and 11 (before 0 and 104; 110 in the `--lower-only` run of `main` that
P2-120 counted causes on), and is no longer among the fifteen.

## Changed code
- Changed pairs 319 of 14560 (before 351); without opaque 154 (before 133); whole-body opaque 8
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 48.3% (before 37.9%)

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 154 | n/a |
| switch-pattern | 31 | P2-122 |
| Conversion | 18 | P2-123 |
| Binary | 8 | P2-087 |
| no-body | 6 | M4-008 (backlog) |
| CollectionExpression | 6 | P2-125 |
| CollectionExpression+Conversion | 5 | P2-125, P2-123 |
| DelegateCreation | 5 | none |
| DefaultValue | 4 | none |
| ImplicitIndexerReference | 4 | none |
| Binary+switch-pattern | 4 | P2-087, P2-122 |
| ImplicitIndexerReference+switch-pattern | 3 | P2-122 |
| ArrayCreation | 3 | none |
| DelegateCreation+switch-pattern | 3 | P2-122 |
| Conversion+DefaultValue+switch-pattern | 3 | P2-123, P2-122 |

`CollectionExpression` is in the reason set of 11 changed pairs. Before, it was in 104.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (unchanged; `-Packages` not run again)

## Verdicts
- By rule: EQ001 14480, EQ002 1, EQ003 101, EQ004 0, EQ005 0, EQ006 0. One pair has no result: it
  crashed in lowering and is listed in `run.properties.unverified`.
- By proofMethod: congruence 14240, bounded 170, lockstep-induction 70
- Unknown by scope: line 19, method 82 (59 over matched pairs plus 23 `unmatched-overload`).
  Line-scoped Unknown share: 18.8%
- Top Unknown reasons: opaque 54 (line 19, method 35), unmatched-overload 23, timeout 19,
  abstraction 3, recursion 1, unaligned-loop 1
- `unbound` Unknowns: 0
- Top abstractions: `delegate` 4, `op:Newtonsoft.Json.Linq.JToken::op_Implicit(string)` 2
- Review list: 20 groups for 102 flagged results; flagged results as a share of matched pairs: 0.7%.
  Top five: `EQ003 unmatched-overload: 23`, `EQ003 timeout: 19`, `EQ003 opaque:DelegateCreation: 8`,
  `EQ003 opaque:CollectionExpression: 6`, `EQ003 opaque:switch-pattern: 6`.

The changed pairs, by verdict:

| | before (465133d), of 351 | this run, of 319 |
|---|---|---|
| Proved Equivalent by the solver | 160 (45.6%): `bounded` 116, `lockstep-induction` 44 | 240 (75.2%): `bounded` 170, `lockstep-induction` 70 |
| Divergent | 28 (8.0%) | 1 (0.3%) |
| Unknown | 163 (46.4%) | 78 (24.5%): opaque 54, timeout 19, abstraction 3, recursion 1, unaligned-loop 1 |

The 23 `unmatched-overload` Unknowns are not matched pairs and are left out.

## Divergent, adjudicated (P2-047's method)

| Cause | EQ002 | Classification |
|---|---|---|
| A build-generated commit constant differs between any two commits (P2-098) | 1 | real, by construction; not a behaviour change of the pull request |

- **Commit constant, 1.** `BugReporter.Program::Main()`, as before.
- **Collection expression, 0 (before 3).** The three pairs P2-120 names are now EQ001 by `bounded`:
  `GitCommandsTests.UserRepositoryHistory.RepositoryXmlSerialiserTests::Serialize_recent_repositories()`,
  `GitCommandsTests.UserRepositoryHistory.Legacy.RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`
  and `GitCommandsTests.ExternalLinks.ExternalLinkRevisionParserTests::GetDefaultRemotes()`.
- **Caller file path, 0 (before 24).** ADR 0046 (P2-098) landed between the two runs.

**The cleanup changed no behaviour that this run found.**

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- P2-120 and the tickets merged since the run before move the solver-proved share of changed pairs
  from 45.6% to 75.2%, and the lowerable share from 37.9% to 48.3%.
- `CollectionExpression` is still opaque in 11 changed pairs, 9 of them in the tests of
  `GitExtUtils.ArgumentBuilder` and of `LazyStringSplit`: spread elements and classes whose elements
  are not added through one `Add`. P2-125.
- The lowering crash on `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`
  is unchanged, and the run exits 5: P2-105.
- 19 timeouts (before 39): P2-101, open.
