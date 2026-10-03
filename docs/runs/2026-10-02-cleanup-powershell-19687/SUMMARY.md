# cleanup run: powershell-19687

- Pair: cleanup, PowerShell/PowerShell PR #19687, legacy 368a1ea3720b, modern 1c55e02df443
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #19687, IDE0019 in the `Microsoft.Management` folder; no runtime change)
- equiv: 46e6636, mode full, wall-clock 287s, exit 5 (one pair crashed in lowering; no load failure)
- `--execute`: not run. Both sides run on net8.0, and this box has Microsoft.NETCore.App 6.0.36,
  10.0.9 and 10.0.12 only. ADR 0040 decision 3 never substitutes another runtime.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 15 | 0.001 | +0.000 |
| load-modern | 15 | 0.000 | +0.000 |
| enumerate | 2 | 1.091 | n/a |
| match | 1 | 0.058 | n/a |
| lower | 33889 | 168.823 | +51.085 |
| verify | 33888 | 78.825 | +48.402 |
| write | 1 | 0.622 | +0.000 |

## Load
- Projects: legacy 15 of 15 C# projects loaded, modern 15 of 15; skipped: none
- Project load rate: 100%
- Detected runtimes (`run.properties.runtimes`): legacy net8.0 on 13 projects (12 from the
  attribute, `Microsoft.PowerShell.SDK` from its host) and netstandard2.0, unhosted, on
  `SMA.Generator`; modern the same. No pair crosses a runtime, so no runtime rule applies.
- Getting it to load took three steps outside `corpus.ps1`, none of which edits a tracked file:
  1. `UseRidGraph=true` in the environment. The projects name `win7-x86` and `win7-x64`, which SDK
     10's portable runtime-identifier graph does not know (NETSDK1083).
  2. A local annotated tag on each checkout. The build runs `git describe`, which fails in a
     depth-1 checkout with no tag.
  3. The repository's own resource generator (`src/ResGen`), run once per side. Without its
     output `System.Management.Automation` has 4,092 unresolved names; the first run, before this
     step, skipped that project on the legacy side and exited 4. That run is void.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 33889 | 33889 |
| analysed lines | 463624 | 463604 |

- Matched pairs 33889; without opaque 26977 (79.6%); whole-body opaque 434 (1.3%); congruent 33748 (99.6%)
- Unchanged share: 99.6% (`pairsCongruent`); the file-level proxy gives 99.5% (1504 of 1513 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 99.6%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none" here means the reason is in no changed pair of this run:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Binary | 2239 | 2239 | none |
| Throw | 1785 | 1785 | none |
| Conversion | 1408 | 1408 | none (in 3 edited pairs) |
| CaughtException | 600 | 600 | none |
| call-throw-in-try | 529 | 529 | none |
| switch-pattern | 466 | 468 | P2-104 (the 3 edited pairs) |
| ArrayElementReference | 460 | 460 | none |
| ref-argument | 353 | 353 | none |
| iterator | 306 | 306 | none |
| InstanceReference | 255 | 255 | none |
| CompoundAssignment | 186 | 186 | none |
| rethrow | 122 | 122 | none |
| DelegateCreation | 119 | 119 | none |
| DefaultValue | 106 | 106 | none |
| unbound | 82 | 82 | P2-106 |

## Changed code
- Changed pairs 140 of 33889; without opaque 6; whole-body opaque 128
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 4.3%
- **Only 12 of the 140 are pairs the pull request edited.** All 12 are in
  `Microsoft.Management.Infrastructure.CimCmdlets`. The other 128 have the same source text on both
  sides: 82 are `unbound` and 46 are `no-body`. A whole-body opaque pair is never congruent, so the
  census counts it as changed (P2-107). Over the 12 edited pairs the lowerable share is 50.0% (6).

Top reason sets ("" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| unbound | 82 | P2-106 |
| no-body | 46 | M4-008 (backlog); P2-107 for counting them as changed |
| "" | 6 | n/a |
| Conversion | 3 | none |
| switch-pattern | 3 | P2-104 |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only

## Verdicts
- By rule: EQ001 33748, EQ002 5, EQ003 135, EQ004 0, EQ005 0, EQ006 0. One pair has no result: it
  crashed in lowering and is listed in `run.properties.unverified`.
- By proofMethod: congruence 33748. No pair was proved by the solver.
- Unknown by scope: line 1, method 134. Line-scoped Unknown share: 0.7%
- Top Unknown reasons: unbound 82, opaque 47 (46 `no-body`, 1 `switch-pattern`), unaligned-loop 2,
  timeout 2, recursion 1, abstraction 1
- `unbound` Unknowns: 82, the same 82 methods on both sides. Building the legacy
  `System.Management.Automation` by hand shows why: the project treats warnings as errors, and
  against the released net8.0 reference pack (the commit pins a .NET 8 preview) it reports
  SYSLIB0051 (54), CS0672 (31) and SYSLIB0050 (2), the obsoletion of formatter-based
  serialization, plus one CS0103. The first three are warnings promoted to errors on bodies that
  bind. That is the likely cause of most of the 82; the SARIF does not record which diagnostic
  unbound a body, so P2-106 confirms it first.
- Top abstractions: `opaque switch-pattern` 1
- Review list: 12 groups for 140 flagged results; flagged results as a share of matched pairs: 0.4%.
  Top five: `EQ003 unbound: 82`, `EQ003 opaque:no-body: 46`, `EQ003 timeout: 2`,
  `EQ003 unaligned-loop: 2`, then five EQ002 groups of 1 each.

The 12 edited pairs, by verdict: proved Equivalent 0, Divergent 5, Unknown 7.

| Procedure (all in `Microsoft.Management.Infrastructure.CimCmdlets`) | Verdict |
|---|---|
| `CimGetInstance::IsClassNameQuerySet(CimBaseCommand)` | EQ002 |
| `CimIndicationWatcher::NewSubscriptionResultHandler(object,CimSubscriptionEventArgs)` | EQ002 |
| `CimRegisterCimIndication::CimIndicationHandler(object,CmdletActionEventArgs)` | EQ002 |
| `ErrorToErrorRecord::ErrorRecordFromAnyException(InvocationContext,System.Exception,CimResultContext)` | EQ002 |
| `CmdletOperationSetCimInstance::WriteObject(object,XOperationContextBase)` | EQ002 |
| `CimAsyncOperation::GetBaseObject(object)` | EQ003 recursion |
| `CimAsyncOperation::GetReferenceOrReferenceArrayObject(object,ref CimType)` | EQ003 abstraction (`opaque switch-pattern`) |
| `CimGetInstance::CreateQuery(CimBaseCommand)` | EQ003 unaligned-loop |
| `CimNewCimInstance::CreateCimInstance(string,string,IEnumerable<string>,IDictionary,NewCimInstanceCommand)` | EQ003 unaligned-loop |
| `CimNewCimInstance::GetCimInstance(CimInstance,XOperationContextBase)` | EQ003 opaque, line-scoped (`switch-pattern`) |
| `CimMethodResultObserver::OnNext(CimMethodResultBase)` | EQ003 timeout |
| `NewCimSessionCommand::BuildSessionOptions(out CimSessionOptions,out CimCredential)` | EQ003 timeout |

## Divergent, adjudicated (P2-047's method)
No replay exists (`--execute` unavailable), so each was hand-traced: both bodies were diffed and
the model was read against them.

| Procedure | Classification | Cause |
|---|---|---|
| `CimGetInstance::IsClassNameQuerySet` | false positive | `as` and `is` are unrelated in the encoding (P2-103) |
| `CimIndicationWatcher::NewSubscriptionResultHandler` | false positive | same |
| `CimRegisterCimIndication::CimIndicationHandler` | false positive | same |
| `ErrorToErrorRecord::ErrorRecordFromAnyException` | false positive | same |
| `CmdletOperationSetCimInstance::WriteObject` | false positive | same |

Confirmed 0, false positive 5, undetermined 0. In each method the only edit turns an `as` cast
followed by a null check into an `is` declaration pattern on the same operand, with the branches
unchanged. The two forms take the same branch on every input. The legacy side's null test reads the
cast's own null flag, and the modern side's test reads the `istype` map, and nothing ties the two
together (M4-005 says the `as` form "does not read `istype`"). So the model has the cast succeed
on one side and the type test fail on the other.

**The cleanup changed no behaviour that this run found.**

## Tests
Not run. The corpus row has no `verifyCommand`, and PowerShell's tests need its full build.

## Findings
- 5 false EQ002 on the `as`-plus-null-check to `is`-pattern rewrite, the whole point of this pull
  request: P2-103.
- `is not T x` is opaque (`switch-pattern`). It accounts for 3 of the 12 edited pairs: P2-104.
- One lowering crash, a null reference, on
  `System.Management.Automation.Security.SystemPolicy::GetFilePolicyEnforcement(string,System.IO.FileStream)`,
  whose source is the same on both sides. It makes the run exit 5: P2-105.
- 82 `unbound` Unknowns whose only diagnostics are warnings promoted to errors: P2-106.
- 128 pairs with identical source on a same-runtime pair are counted as changed because they are
  whole-body opaque, which puts the lowerable share at 4.3% where the edited pairs give 50.0%:
  P2-107.
- No edited pair was proved by the solver. Of the 7 Unknown, 3 are loop or recursion alignment
  failures (in `GetBaseObject` and `CreateQuery` the unpaired header states are the cast's local
  and its null flag) and 2 are timeouts. Whether P2-103 also decides these is for that ticket to measure.
- `-Fetch` did not patch a `global.json` whose `rollForward` stays inside a major. Fixed in this
  PR (`corpus.ps1`).
