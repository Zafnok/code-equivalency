# cleanup run: gitextensions-11284

- Pair: cleanup, gitextensions/gitextensions PR #11284, legacy ba7b679180c4, modern 89962d9f88b7
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #11284, IDE0008 explicit type in place of `var`; no runtime change)
- equiv: 78853202, mode cleanup (`full`), compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), no `--jobs` (24 processors, so 24 pairs at a time), wall-clock 229s (3m49s), exit 1. Exit 1 means a Divergent result exists. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else.
- A second run in compare mode thorough followed on the same build and checkouts. Every number below is the quick run's unless it says thorough; the thorough run's differences are under "Thorough run".
- `--execute`: not run (ticket P2-110, Out of scope).
- Ticket P2-110. The earlier summary of this pair is `docs/runs/2026-10-02-cleanup-gitextensions-11284/SUMMARY.md` (P2-058, equiv 46e6636, before the compare modes, one pair at a time, 2668s, exit 5). "Before" below is that run.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 46 | 0.001 | +0.001 |
| load-modern | 46 | 0.000 | +0.000 |
| enumerate | 2 | 0.448 | n/a |
| match | 1 | 0.018 | n/a |
| lower | 14586 | 70.536 | +30.650 |
| verify | 14586 | 125.532 | -98.273 |
| write | 1 | 0.258 | +0.000 |

The two slowest pairs are
`TranslationApp.TranslationHelpers::LoadTranslation(System.Collections.Generic.IDictionary<string, global::ResourceManager.Xliff.TranslationFile>,System.Collections.Generic.IDictionary<string, global::System.Collections.Generic.List<global::TranslationApp.TranslationItemWithCategory>>)`
at 114 seconds and `GitCommands.Config.ConfigFile::GetAsString()` at 110 seconds, both Unknown.

## Load
- Projects: legacy 46 of 46 C# projects loaded, modern 46 of 46; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Detected runtimes (`run.properties.runtimes`): legacy net6.0 on 45 projects (44 from the target framework attribute, 1 from its host) and netstandard2.0, unhosted, on 1; modern the same. No pair crosses a runtime.
- Both sides were restored with `dotnet restore --force`.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14586 | 14586 |
| analysed lines | 194781 | 194796 |

- Matched pairs 14586; without opaque 12357 (84.7%); whole-body opaque 136 (0.9%); congruent 14408 (98.8%, before 98.5%)
- Unchanged share: 98.8% (`pairsCongruent`); the file-level proxy gives 57.9% of lines (1227 of 1695 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 98.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were looked up among the open tickets only.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 439 | 439 | none |
| Conversion | 269 | 269 | none |
| DelegateCreation | 246 | 246 | none |
| Binary | 226 | 226 | none |
| LocalFunction | 218 | 218 | none |
| switch-pattern | 214 | 214 | none |
| CaughtException | 120 | 120 | none |
| ImplicitIndexerReference | 119 | 119 | none |
| Layout | 115 | 115 | none |
| DeconstructionAssignment | 79 | 79 | none |
| InterpolatedString | 77 | 77 | P2-102 |
| iterator | 77 | 77 | none |
| ArrayElementReference | 68 | 68 | none |
| ref-argument | 67 | 67 | none |
| no-body | 59 | 59 | P2-165 (pairs that cross a runtime only) |

## Changed code
- Changed pairs 178 of 14586 (before 212 of 14532); without opaque 56 (before 39); whole-body opaque 12
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 31.5% (before 18.4%)
- 173 of the 178 are in files the pull request edited. The other 5 are in byte-identical files:
  `BugReporter.Program::Main()` (the Divergent below), 3 `no-body` constructors and 1 pair the
  solver proves (`GitUI.UserControls.RevisionGrid.CopyContextMenuItem::AddItem(string,string,System.Drawing.Image,char?)`).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 56 | n/a |
| DeconstructionAssignment | 28 | none |
| LocalFunction | 10 | none |
| DelegateCreation | 9 | none |
| ImplicitIndexerReference | 8 | none |
| iterator | 7 | none |
| no-body | 5 | none on a same-runtime pair |
| CaughtException | 4 | none |
| ArrayCreation | 3 | none |
| ArrayElementReference | 3 | none |
| Binary | 2 | none |
| Binary+DeconstructionAssignment | 2 | none |
| CaughtException+DeconstructionAssignment+DelegateCreation | 2 | none |
| switch-pattern | 2 | none |
| ArrayCreation+ImplicitIndexerReference+switch-pattern | 1 | none |

A reason is in the reason set of this many changed pairs: `DeconstructionAssignment` 46 (25.8%),
`DelegateCreation` 21, `LocalFunction` 18, `ImplicitIndexerReference` 14, `CaughtException` 13.
The pull request gives 38 deconstructions written with `var` their explicit types, and a
deconstruction is opaque in either spelling.

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (`-Packages`)

## Verdicts
- By rule: EQ001 14481, EQ002 1, EQ003 126, EQ004 0, EQ005 0, EQ006 0. Every matched pair has a result.
- By proofMethod: congruence 14408, lockstep-induction 52, bounded 21
- Unknown by scope: line 37, method 89 (67 over matched pairs plus 22 `unmatched-overload`).
  Line-scoped Unknown share: 29.4%
- Top Unknown reasons: opaque 82 (line 37, method 45), unmatched-overload 22, timeout 17,
  abstraction 5
- `unbound` Unknowns: 0
- Top abstractions: `delegate` 12
- Review list: 23 groups for 127 flagged results; flagged results as a share of matched pairs: 0.9%.
  Top five: `EQ003 opaque:DeconstructionAssignment: 34`, `EQ003 unmatched-overload: 22`,
  `EQ003 timeout: 17`, `EQ003 opaque:DelegateCreation: 9`, `EQ003 opaque:LocalFunction: 8`.
- All 17 timeouts end on the resource limit (`run.properties.queryEndings`: resourceLimit 65,
  wallClock 0).

The changed pairs, by verdict:

| | before (46e6636), of 212 | this run (78853202, quick), of 178 |
|---|---|---|
| Proved Equivalent by the solver | 50 (23.6%): lockstep-induction 45, bounded 5 | 73 (41.0%): lockstep-induction 52, bounded 21 |
| Divergent | 22 (10.4%) | 1 (0.6%) |
| Unknown | 140 (66.0%): opaque 110, timeout 17, abstraction 9, unaligned-loop 4 | 104 (58.4%): opaque 82, timeout 17, abstraction 5 |

The 22 `unmatched-overload` Unknowns are not matched pairs and are left out.

## Divergent, adjudicated (P2-047's method)
No replay was run, so the one result was hand-traced: the file is the same on both sides, and the
first call at which the model's two traces part was read against the body.

| Cause | EQ002 | Classification |
|---|---|---|
| A build-generated commit constant differs between any two commits | 1 | real, by construction; not a behaviour change of the pull request |

Confirmed behaviour changes 0, false positive 0, real by construction 1, undetermined 0.

- `BugReporter.Program::Main()`: the same procedure and the same cause as on `gitextensions-11372`.
  The first argument that differs between the two traces is the string passed to
  `BugReporter.UserEnvironmentInformation::Initialise(string,bool)`, read from a generated
  `ThisAssembly` constant holding the commit. The procedure was not among this pair's 22 EQ002 in
  the run before, so on this pair the result is new; `gitextensions-11372` has reported it since
  P2-058.
- All 22 EQ002 of the run before named a build location. None does now.

**The cleanup changed no behaviour that this run found.**

## Thorough run
Compare mode thorough, same build and checkouts, no `--jobs`: 3357s (56m), exit 1, no crash (no
`run.properties.unverified`, 0 notifications). The budget pass took 50 pairs (2952 s) and the IL
pass 27 (132 s). No Equivalent or Divergent of the quick run changes.

| | quick | thorough |
|---|---|---|
| Proved Equivalent by the solver | 73 (41.0%) | 95 (53.4%): lockstep-induction 60, bounded 35 |
| Divergent | 1 (0.6%) | 1 (0.6%) |
| Unknown | 104 (58.4%): opaque 82, timeout 17, abstraction 5 | 82 (46.1%): opaque 72, abstraction 5, timeout 5 |

- `decidedBy`: `il-pass` 22 EQ001, all `opaque` in the quick run; `budget-pass` 12 EQ003, each a
  `timeout` of the quick run that is `opaque` now.
- `EQ003 opaque:DeconstructionAssignment` falls from 34 to 18 in the review list.
- The one Divergent is `BugReporter.Program::Main()`, as in the quick run.

## Tests
Not run. The corpus row has no `verifyCommand`.

## Findings
- No lowering crash. The three procedures that made the run before exit 5 have results (P2-105).
- The solver proves 73 of 178 changed pairs (41.0%), from 50 of 212 (23.6%). Divergent is 1, from 22.
- `DeconstructionAssignment` is in the reason set of 46 changed pairs (25.8%) and is the first
  reason of 34 Unknowns, the largest group of the review list. No open ticket owns it (P2-025,
  which made it a named reason, is done).
- 17 timeouts (9.6% of changed pairs), the same count as before, all on the resource limit.
