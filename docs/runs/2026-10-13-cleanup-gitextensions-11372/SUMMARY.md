# cleanup run: gitextensions-11372

- Pair: cleanup, gitextensions/gitextensions PR #11372, legacy 030255ef17cb, modern 1cfb0e4441e2
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11372, IDE0028 collection expressions; no runtime change)
- equiv: 78853202, mode cleanup (`full`), compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), no `--jobs` (24 processors, so 24 pairs at a time), wall-clock 259s (4m19s), exit 1. Exit 1 means a Divergent result exists. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else.
- A second run in compare mode thorough followed on the same build and checkouts. Every number below is the quick run's unless it says thorough; the thorough run's differences are under "Thorough run".
- `--execute`: not run (ticket P2-110, Out of scope). Both sides run on net8.0, which this box does not have.
- Ticket P2-110. Earlier summaries of this pair: `docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md` (P2-058, equiv 46e6636), `docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md` (P2-099's branch) and `docs/runs/2026-10-03-cleanup-gitextensions-11372-p2-120/SUMMARY.md` (P2-120's branch). All three were before the compare modes (ADR 0049), one pair at a time, at resourceLimit 5000000. "Before" below is P2-058's run unless it names another.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.001 | +0.001 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.489 | n/a |
| match | 1 | 0.018 | n/a |
| lower | 14614 | 71.625 | +31.618 |
| verify | 14614 | 151.423 | -82.453 |
| write | 1 | 0.264 | +0.000 |

Verify took 16,830 seconds in P2-058's run and 1,713 in P2-099's. The slowest pair is
`TranslationApp.TranslationHelpers::LoadTranslation(System.Collections.Generic.IDictionary<string, global::ResourceManager.Xliff.TranslationFile>,System.Collections.Generic.IDictionary<string, global::System.Collections.Generic.List<global::TranslationApp.TranslationItemWithCategory>>)`
at 120 seconds (Unknown).

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes (`run.properties.runtimes`): legacy net8.0 on 45 projects (44 from the target framework attribute, 1 from its host) and netstandard2.0, unhosted, on 1; modern the same. No pair crosses a runtime.
- Both sides were restored with `dotnet restore --force` (the ticket's Notes; `MSBuild -t:restore` fails on them with MSB4018).

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14614 | 14614 |
| analysed lines | 195108 | 195130 |

- Matched pairs 14614; without opaque 12367 (84.6%, before 68.7%); whole-body opaque 137 (0.9%); congruent 14295 (97.8%)
- Unchanged share: 97.8% (`pairsCongruent`); the file-level proxy gives 70.6% of lines (1511 of 1707 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were looked up among the open tickets only.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 442 | 442 | none |
| Conversion | 249 | 249 | none |
| DelegateCreation | 248 | 248 | none |
| Binary | 243 | 243 | none |
| LocalFunction | 219 | 219 | none |
| switch-pattern | 213 | 213 | none |
| CaughtException | 120 | 120 | none |
| ImplicitIndexerReference | 120 | 120 | none |
| Layout | 118 | 118 | none |
| InterpolatedString | 80 | 80 | P2-102 |
| DeconstructionAssignment | 79 | 79 | none |
| iterator | 77 | 77 | none |
| ArrayElementReference | 68 | 68 | none |
| ref-argument | 61 | 61 | none |
| no-body | 60 | 60 | P2-165 (pairs that cross a runtime only) |

`CollectionExpression` is 0 on the legacy side and 11 on the modern side (P2-128). `Conversion` was
2151 and 2174 in P2-058's run, and `switch-pattern` 1246 on both sides.

## Changed code
- Changed pairs 319 of 14614 (before 351 of 14560); without opaque 182 (before 23); whole-body opaque 8
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 57.1% (before 6.6%)
- 311 of the 319 are in files the pull request edited. The other 8 are in byte-identical files:
  `BugReporter.Program::Main()` (the Divergent below), 5 `no-body` constructors, 1 `abstraction`
  Unknown (`GitExtensions.Program::Main()`) and 1 pair the solver proves
  (`GitCommands.PathUtil::GetRepositoryName(string)`).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 182 | n/a |
| LocalFunction | 20 | none |
| Conversion | 10 | none |
| DelegateCreation | 10 | none |
| Binary | 9 | none |
| ImplicitIndexerReference | 8 | none |
| CollectionExpression | 6 | P2-128 |
| no-body | 6 | none on a same-runtime pair |
| CollectionExpression+Conversion+rebound-call | 5 | P2-128 |
| ArrayCreation | 3 | none |
| CaughtException | 3 | none |
| DelegateCreation+ImplicitIndexerReference | 2 | none |
| DelegateCreation+LocalFunction | 2 | none |
| ImplicitIndexerReference+LocalFunction | 2 | none |
| InterpolatedString | 2 | P2-102 |

A reason is in the reason set of this many changed pairs: `LocalFunction` 43 (13.5%),
`DelegateCreation` 25, `ImplicitIndexerReference` 19, `Conversion` 18, `Binary` 17,
`CollectionExpression` 11 (104 in P2-099's run, 11 in P2-120's).

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (`-Packages`, which works again
  after P2-167)

## Verdicts
- By rule: EQ001 14512, EQ002 1, EQ003 124, EQ004 0, EQ005 0, EQ006 0. Every matched pair has a result.
- By proofMethod: congruence 14295, bounded 153, lockstep-induction 64
- Unknown by scope: line 31, method 93 (70 over matched pairs plus 23 `unmatched-overload`).
  Line-scoped Unknown share: 25.0%
- Top Unknown reasons: opaque 69 (line 31, method 38), timeout 26, unmatched-overload 23,
  abstraction 4, unaligned-loop 1, recursion 1
- `unbound` Unknowns: 0
- Top abstractions: `delegate` 8, `op:Newtonsoft.Json.Linq.JToken::op_Implicit(string)` 2
- Review list: 22 groups for 125 flagged results; flagged results as a share of matched pairs: 0.9%.
  Top five: `EQ003 timeout: 26`, `EQ003 unmatched-overload: 23`, `EQ003 opaque:LocalFunction: 21`,
  `EQ003 opaque:DelegateCreation: 7`, `EQ003 opaque:CollectionExpression: 6`.
- All 26 timeouts end on the resource limit (`run.properties.queryEndings`: resourceLimit 96,
  wallClock 0), so they do not depend on how busy the box was.

The changed pairs, by verdict:

| | P2-058 (46e6636), of 351 | P2-120's branch (259d051), of 319 | this run (78853202, quick), of 319 |
|---|---|---|---|
| Proved Equivalent by the solver | 7 (2.0%): bounded 7 | 240 (75.2%): bounded 170, lockstep-induction 70 | 217 (68.0%): bounded 153, lockstep-induction 64 |
| Divergent | 25 (7.1%) | 1 (0.3%) | 1 (0.3%) |
| Unknown | 319 (90.9%): opaque 261, timeout 43, unaligned-loop 7, abstraction 6, recursion 2 | 78 (24.5%): opaque 54, timeout 19, abstraction 3, recursion 1, unaligned-loop 1 | 101 (31.7%): opaque 69, timeout 26, abstraction 4, unaligned-loop 1, recursion 1 |

The 23 `unmatched-overload` Unknowns are not matched pairs and are left out.

This run proves 23 fewer pairs than P2-120's branch did. Two things changed in between and both
point that way: quick mode's resource limit is 2000000 where that run had 5000000 (7 more
timeouts), and a call to a local function is now an opaque with reason `LocalFunction`, which
P2-127 added to close a false-Equivalent path (15 more `opaque`).

## Divergent, adjudicated (P2-047's method)
No replay exists (`--execute` not run), so the one result was hand-traced: the file is the same on
both sides, and the first call at which the model's two traces part was read against the body.

| Cause | EQ002 | Classification |
|---|---|---|
| A build-generated commit constant differs between any two commits | 1 | real, by construction; not a behaviour change of the pull request |

Confirmed behaviour changes 0, false positive 0, real by construction 1, undetermined 0.

- `BugReporter.Program::Main()`, as in every earlier run of this pair. The two traces make the same
  calls in the same order, and the first argument that differs is the string passed to
  `BugReporter.UserEnvironmentInformation::Initialise(string,bool)`, which the body reads from a
  generated `ThisAssembly` constant holding the commit. The pull request did not touch the file.
- The 24 caller-file-path EQ002 of the earlier runs are gone (P2-098): 0 Divergent in this run names
  a build location.

**The cleanup changed no behaviour that this run found.**

## Thorough run
Compare mode thorough, same build and checkouts, no `--jobs`: 3846s (1h04m), exit 1, no crash (no
`run.properties.unverified`, 0 notifications). The budget pass took 58 pairs (3266 s) and the IL
pass 18 (294 s). No Equivalent or Divergent of the quick run changes.

| | quick | thorough |
|---|---|---|
| Proved Equivalent by the solver | 217 (68.0%) | 222 (69.6%): bounded 157, lockstep-induction 65 |
| Divergent | 1 (0.3%) | 4 (1.3%) |
| Unknown | 101 (31.7%): opaque 69, timeout 26, abstraction 4, unaligned-loop 1, recursion 1 | 93 (29.2%): opaque 70, timeout 18, abstraction 4, unaligned-loop 1 |

- `decidedBy`: `il-pass` 5 EQ001 and 3 EQ002; `budget-pass` 8 EQ003, each a `timeout` of the quick
  run that is `opaque` now.
- The 3 EQ002 the IL pass adds are false positives:
  `GitExtUtilsTests.ArgumentBuilderTests::Add(string[],int,string)`,
  `GitExtUtilsTests.LazyStringSplitTests::None(string,char,string[])` and
  `GitExtUtilsTests.LazyStringSplitTests::RemoveEmptyEntries(string,char,string[])`. Each replaces an
  empty collection and a `foreach` that calls `Add` with a collection expression holding one spread.
  In the counterexample the modern side throws a `NullReferenceException` where the legacy side
  goes on, at a point where neither source form dereferences anything the other does not. P2-169.

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- No lowering crash. `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`,
  which made every earlier run of this pair exit 5, has a result (P2-105).
- The solver proves 217 of 319 changed pairs (68.0%), from 7 of 351 (2.0%). Divergent is 1, from 25.
- `LocalFunction` is in the reason set of 43 changed pairs (13.5%) and is the first reason of 21
  Unknowns. No open ticket owns it; P2-127 made it opaque on purpose, for soundness.
- 26 timeouts (8.2% of changed pairs), all on the resource limit. The thorough run's budget pass
  proves none of them and moves 8 to `opaque`.
- `CollectionExpression` is left in 11 changed pairs: P2-128, open.
- Thorough mode only: 3 false EQ002 from the IL pass on a collection expression with a spread: P2-169.
