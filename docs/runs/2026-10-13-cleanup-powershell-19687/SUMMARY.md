# cleanup run: powershell-19687

- Pair: cleanup, PowerShell/PowerShell PR #19687, legacy 368a1ea3720b, modern 1c55e02df443
- Corpus list: `tools/corpus/pairs.csv` (cleanup pair)
- Migrated by: human (upstream PR #19687, IDE0019 in the `Microsoft.Management` folder; no runtime change)
- equiv: 78853202, mode cleanup (`full`), compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), no `--jobs` (24 processors, so 24 pairs at a time), wall-clock 183s (3m03s), exit 0. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else.
- A second run in compare mode thorough followed on the same build and checkouts. Every number below is the quick run's unless it says thorough; the thorough run's differences are under "Thorough run".
- `--execute`: not run (ticket P2-110, Out of scope). Both sides run on net8.0, which this box does not have.
- Ticket P2-110. The earlier summary of this pair is `docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md` (P2-058, equiv 46e6636, before the compare modes, one pair at a time, 287s, exit 5). "Before" below is that run.

## Phase times

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 15 | 0.001 | +0.000 |
| load-modern | 15 | 0.000 | +0.000 |
| enumerate | 2 | 0.911 | n/a |
| match | 1 | 0.042 | n/a |
| lower | 34183 | 145.083 | +42.529 |
| verify | 34183 | 10.223 | +2.860 |
| write | 1 | 0.583 | +0.000 |

## Load
- Projects: legacy 15 of 15 C# projects loaded, modern 15 of 15; skipped: none
- Project load rate: 100%
- Not built: none on either side
- Detected runtimes (`run.properties.runtimes`): legacy net8.0 on 13 projects (12 from the target framework attribute, 1 from its host) and netstandard2.0, unhosted, on 1; modern the same. No pair crosses a runtime.
- The three steps `tools/corpus/README.md` lists for this pair were done as written (`UseRidGraph`,
  a local tag on each checkout, the repository's resource generator once per side), then
  `dotnet restore --force` on both sides. `git status --porcelain` is empty on both checkouts.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 34183 | 34183 |
| analysed lines | 463624 | 463604 |

- Matched pairs 34183 (before 33889); without opaque 28419 (83.1%, before 79.6%); whole-body opaque 663 (1.9%); congruent 34146 (99.9%, before 99.6%)
- Unchanged share: 99.9% (`pairsCongruent`); the file-level proxy gives 99.5% of lines (1504 of 1513 files)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 99.9%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were looked up among the open tickets only.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Throw | 1789 | 1789 | none |
| Binary | 1057 | 1057 | none |
| Conversion | 731 | 731 | none |
| CaughtException | 573 | 573 | none |
| call-throw-in-try | 530 | 530 | none |
| ArrayElementReference | 460 | 460 | none |
| Layout | 385 | 385 | none |
| ref-argument | 350 | 350 | none |
| no-body | 340 | 340 | P2-165 (pairs that cross a runtime only) |
| iterator | 306 | 306 | none |
| InstanceReference | 233 | 233 | none |
| rethrow | 122 | 122 | none |
| CompoundAssignment | 121 | 121 | none |
| DelegateCreation | 118 | 118 | none |
| undefined | 68 | 68 | none |

`switch-pattern`, 466 and 468 before, is no longer among the first fifteen, and `unbound` is 17 on
each side, from 82.

## Changed code
- Changed pairs 37 of 34183 (before 140 of 33889); without opaque 13; whole-body opaque 18
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 35.1% (before 4.3%)
- **12 of the 37 are pairs the pull request edited**, the same 12 as before, all in
  `Microsoft.Management.Infrastructure.CimCmdlets`.
- The other 25 are in byte-identical files: 17 `unbound` (82 before; P2-106), 3 pairs the solver
  proves, 3 `opaque` Unknowns and 2 `timeout` Unknowns. The 46 identical `no-body` pairs of the run
  before are no longer changed pairs (P2-107).

Reason sets ("" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| unbound | 17 | none (P2-106 is done; these are what it left) |
| "" | 13 | n/a |
| ArrayElementReference+Binary+CaughtException+DefaultValue+Throw+call-throw-in-try | 1 | none |
| Binary | 1 | none |
| Binary+Throw | 1 | none |
| CaughtException | 1 | none |
| CaughtException+call-throw-in-try | 1 | none |
| ImplicitIndexerReference | 1 | none |
| iterator | 1 | none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (`-Packages`)

## Verdicts
- By rule: EQ001 34156, EQ002 0, EQ003 27, EQ004 0, EQ005 0, EQ006 0. Every matched pair has a result.
- By proofMethod: congruence 34146, bounded 8, lockstep-induction 2
- Unknown by scope: line 0, method 27. Line-scoped Unknown share: 0%
- Top Unknown reasons: unbound 17, opaque 3 (`iterator`, `CaughtException`, and one whose base
  obligation reaches an opaque node), timeout 3, unaligned-loop 3, recursion 1
- `unbound` Unknowns: 17, the same 17 methods on both sides, in files the pull request did not
  touch (12 in `System.Management.Automation`, 5 in `Microsoft.PowerShell.ConsoleHost`). The modern
  side is a human commit, so each is a finding; P2-106, which took the count from 82, is done and no
  open ticket owns the rest.
- Top abstractions: none (no `abstraction` Unknown)
- Review list: 7 groups for 27 flagged results; flagged results as a share of matched pairs: 0.1%.
  Top five: `EQ003 unbound: 17`, `EQ003 timeout: 3`, `EQ003 unaligned-loop: 3`, then three
  `EQ003 opaque` groups of 1 each (`CaughtException`, `Throw`, `iterator`).

The 12 edited pairs, by verdict:

| | before (46e6636) | this run (78853202, quick) |
|---|---|---|
| Proved Equivalent by the solver | 0 | 7 (58.3%): bounded 6, lockstep-induction 1 |
| Divergent | 5 (41.7%) | 0 |
| Unknown | 7 (58.3%): unaligned-loop 2, timeout 2, recursion 1, abstraction 1, opaque 1 | 5 (41.7%): unaligned-loop 3, recursion 1, timeout 1 |

| Procedure (all in `Microsoft.Management.Infrastructure.CimCmdlets`) | Before | This run |
|---|---|---|
| `CimGetInstance::IsClassNameQuerySet(CimBaseCommand)` | EQ002 | EQ001 bounded |
| `CimIndicationWatcher::NewSubscriptionResultHandler(object,CimSubscriptionEventArgs)` | EQ002 | EQ001 bounded |
| `CimRegisterCimIndication::CimIndicationHandler(object,CmdletActionEventArgs)` | EQ002 | EQ001 bounded |
| `ErrorToErrorRecord::ErrorRecordFromAnyException(InvocationContext,System.Exception,CimResultContext)` | EQ002 | EQ001 bounded |
| `CmdletOperationSetCimInstance::WriteObject(object,XOperationContextBase)` | EQ002 | EQ001 bounded |
| `CimNewCimInstance::GetCimInstance(CimInstance,XOperationContextBase)` | EQ003 opaque, line-scoped (`switch-pattern`) | EQ001 bounded |
| `CimMethodResultObserver::OnNext(CimMethodResultBase)` | EQ003 timeout | EQ001 lockstep-induction |
| `CimAsyncOperation::GetBaseObject(object)` | EQ003 recursion | EQ003 recursion |
| `CimAsyncOperation::GetReferenceOrReferenceArrayObject(object,ref CimType)` | EQ003 abstraction (`opaque switch-pattern`) | EQ003 unaligned-loop |
| `CimGetInstance::CreateQuery(CimBaseCommand)` | EQ003 unaligned-loop | EQ003 unaligned-loop |
| `CimNewCimInstance::CreateCimInstance(string,string,IEnumerable<string>,IDictionary,NewCimInstanceCommand)` | EQ003 unaligned-loop | EQ003 unaligned-loop |
| `NewCimSessionCommand::BuildSessionOptions(out CimSessionOptions,out CimCredential)` | EQ003 timeout | EQ003 timeout |

Over all 37 changed pairs: proved Equivalent 10 (27.0%), Unknown 27 (73.0%), Divergent 0.

## Divergent, adjudicated (P2-047's method)
There is none. The five EQ002 of the run before, all on an `as` cast plus null check against an
`is` pattern, are proved Equivalent (P2-103).

**The cleanup changed no behaviour that this run found.**

## Thorough run
Compare mode thorough, same build and checkouts, no `--jobs`: 470s (7m50s), exit 0, no crash (no
`run.properties.unverified`, 0 notifications). The budget pass took 9 pairs (243 s); the IL pass
took none. No Equivalent of the quick run changes, and there is no Divergent.

- `decidedBy: budget-pass`, 2 results. `NewCimSessionCommand::BuildSessionOptions(out CimSessionOptions,out CimCredential)`,
  one of the 12 edited pairs, is proved (`bounded`) where the quick run timed out, so the edited
  pairs are 8 proved (66.7%) and 4 Unknown (unaligned-loop 3, recursion 1).
  `Microsoft.PowerShell.Commands.NewPSDriveCommand::ProcessRecord()`, in a byte-identical file, goes
  from `timeout` to `opaque`.
- Over all 37 changed pairs: proved 11 (bounded 9, lockstep-induction 2), Unknown 26 (unbound 17,
  opaque 4, unaligned-loop 3, timeout 1, recursion 1), Divergent 0.

## Tests
Not run. The corpus row has no `verifyCommand`, and PowerShell's tests need its full build.

## Findings
- No lowering crash.
  `System.Management.Automation.Security.SystemPolicy::GetFilePolicyEnforcement(string,System.IO.FileStream)`,
  which made the run before exit 5, has a result (P2-105). This is the first run of the pair that
  exits 0.
- The pull request's own rewrite is proved: 7 of the 12 edited pairs, from 0, and no Divergent, from
  5 (P2-103). `is not T t` is no longer opaque in any of them (P2-104).
- The 5 edited pairs still Unknown are 3 `unaligned-loop`, 1 `recursion` and 1 `timeout`: loop and
  recursion alignment across the rewrite, not a missing construct.
- 17 `unbound` pairs remain, from 82, in files the pull request did not touch. No open ticket.
- 8 pairs in byte-identical files are not congruent and not `unbound`: 3 the solver proves, 3
  `opaque` and 2 `timeout`. The SARIF does not say what keeps an unedited body out of congruence on
  a same-runtime pair, so the cause is not known from this run.
