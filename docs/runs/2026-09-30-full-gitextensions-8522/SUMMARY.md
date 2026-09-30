# full+seeded+execute run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream PR #8522, .NET Framework 4.8 to .NET 5)
- equiv: bd8e379, wall-clock full 32211s (8h56m) exit 1; seeded 32143s (8h55m) exit 1; seeded-mech 44832s (12h27m) exit 1; full --execute 32237s (8h57m) exit 1. Exit 1 means Divergent results exist; no run had a pair-level crash (exit 5) or a load failure (exit 4). The Git Extensions runs shared the machine and ran concurrently. The first `full` and `full --execute` pair collided on MSBuild file locks (skipped projects, exit 4), so both were discarded and rerun one after the other; see the verdict file.

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.000 | +0.000 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.584 | n/a |
| match | 1 | 0.026 | n/a |
| lower | 13541 | 95.958 | +50.780 |
| verify | 13541 | 24456.551 | +3484.798 |
| write | 1 | 0.277 | +0.000 |

## Load
- Projects: legacy 48 of 48 C# projects loaded, modern 43 of 43; skipped: none
- Project load rate: 100% (0 skipped of the C# projects the solution builds)
- Not built (outside the default configuration, ADR 0028 clarification 2026-09-24): legacy 1 (`Setup`), modern 1 (`Setup`)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13708 | 13556 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13541; without opaque 8966 (66.2%); whole-body opaque 158 (1.2%); congruent 12398 (91.6%)
- Unchanged share: 91.6% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 91.6%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 1700 | 1699 | none open (M4-004 shares identical fragments; this run files P2-067) |
| switch-pattern | 1199 | 1196 | P1-014, P1-015, P1-016 (ADR 0039 IL fallback) |
| Conversion | 763 | 762 | P1-014 (ADR 0039) |
| Binary | 652 | 652 | P1-014 (ADR 0039) |
| InstanceReference | 374 | 374 | none (0.3% of changed pairs, below 5%) |
| InterpolatedString | 331 | 329 | P1-014 (ADR 0039) |
| DefaultValue | 315 | 315 | none (0.7%, below 5%) |
| CaughtException | 124 | 123 | none (1.1%, below 5%) |
| DeconstructionAssignment | 80 | 80 | P1-014 (ADR 0039) |
| iterator | 73 | 73 | none (1.2%, below 5%) |
| ArrayElementReference | 62 | 62 | none (0.4%, below 5%) |
| CompoundAssignment | 58 | 58 | P1-014 (ADR 0039) |
| Tuple | 48 | 48 | none (0.5%, below 5%) |
| ArrayCreation | 42 | 42 | none (0.7%, below 5%) |
| ref-argument | 26 | 26 | none (0.1%, below 5%) |

## Changed code
- Changed pairs 1143 of 13541; without opaque 447; whole-body opaque 14
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 39.1%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 447 | n/a |
| "DelegateCreation" | 194 | none open (M4-004 shares identical fragments; this run files P2-067) |
| "switch-pattern" | 79 | P1-014, P1-015, P1-016 (ADR 0039 IL fallback) |
| "Binary" | 39 | P1-014 (ADR 0039) |
| "DelegateCreation+switch-pattern" | 34 | P1-014, P1-015, P1-016 (ADR 0039 IL fallback); none open (M4-004 shares identical fragments; this run files P2-067) |
| "InterpolatedString" | 26 | P1-014 (ADR 0039) |
| "Conversion" | 22 | P1-014 (ADR 0039) |
| "DelegateCreation+InterpolatedString" | 16 | P1-014 (ADR 0039); none open (M4-004 shares identical fragments; this run files P2-067) |
| "Conversion+DelegateCreation" | 15 | P1-014 (ADR 0039); none open (M4-004 shares identical fragments; this run files P2-067) |
| "iterator" | 14 | none (1.2%, below 5%) |
| "CaughtException" | 13 | none (1.1%, below 5%) |
| "InterpolatedString+switch-pattern" | 10 | P1-014 (ADR 0039); P1-014, P1-015, P1-016 (ADR 0039 IL fallback) |
| "Binary+Conversion" | 9 | P1-014 (ADR 0039) |
| "CompoundAssignment" | 9 | P1-014 (ADR 0039) |
| "ArrayCreation" | 8 | none (0.7%, below 5%) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1223 | 1224 |
| distinct members | 208 | 206 |
| pairs with any | 684 | 686 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only. Version-changed (first 15): adystech.credentialmanager 1.7.0 -> 2.1.1; appinsights.windowsdesktop 2.10.42-preview -> 2.13.1; ben.demystifier 0.1.4 -> 0.1.6; castle.core 4.2.0 -> 4.4.0; excss 2.0.6 -> 4.1.0; fluentassertions 5.2.0 -> 5.10.3; jetbrains.annotations 2018.2.1 -> 2020.1.0; libgit2sharp 0.25.0 -> 0.26.2; libgit2sharp.nativebinaries 1.0.210 -> 2.0.306; microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1; microsoft.codecoverage 15.9.0 -> 16.6.1; microsoft.net.test.sdk 15.9.0 -> 16.6.1; microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0; microsoft.visualstudio.composition 15.6.36 -> 16.4.11; microsoft.visualstudio.threading 16.5.132 -> 16.7.56

## Verdicts (full, no --execute)
- By rule: EQ001 12475, EQ002 84, EQ003 726, EQ004 15, EQ005 167, EQ006 275
- By proofMethod: bounded 64, congruence 12398, lockstep-induction 13, n/a 1267; solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`): 77
- Unknown by scope: line 145, method 581. Line-scoped Unknown share: 20.0% (145 of 726)
- Top Unknown reasons: abstraction 238, timeout 206, opaque 193, unaligned-loop 66, unmatched-overload 19, recursion 4
- Top abstractions (238 `abstraction` Unknown results; entries of `properties.abstractions` by kind, top 15; `opaque` is one kind because the SARIF records a fragment's fingerprint and not its reason, and 227 distinct fragments are behind it): opaque 503, conv.i32.f32 27, op:System.String::op_Equality(string,string) 22, conv.f64.i32 21, f32.mul 20, conv.f32.i32 19, op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder) 18, f32.div 10, op:System.IntPtr::op_Equality(System.IntPtr,System.IntPtr) 9, f32.sub 8, f32.add 8, conv.i32.f64 8, op:GitExtUtils.ArgumentString::op_Implicit(string) 6, conv.f32.f64 6, op:System.Type::op_Equality(System.Type,System.Type) 4
- Pair-level crashes: 0 tool-execution notifications, 0 unverified procedures

## Tests (full only)
- The human pair has no `verify_command` in the manifest, and the upstream suite is WinForms UI tests that need a desktop session and a Git install; not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a (criterion 4 of M4-007 cannot be evaluated for this pair)

## Seeds (seeded, hand-written; the `tools/corpus/seeds.md` catalogue, the same methods as M4-007 because its seeded copies were not kept)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S01 | `System.StringExtensions::RemovePrefix(string,string,System.StringComparison)` | EQ006 Divergent (runtime-change) | n/a |
| S02 | `GitCommands.PathUtil::GetFileName(string)` | EQ002 Divergent | n/a |
| S03 | `GitCommands.Git.GitRevisionTester::Matches(GitCommands.GitRevision,string)` | EQ003 Unknown (opaque, line-scoped) | yes |
| S04 | `GitCommands.LockableNotifier::CheckNotify(int)` | EQ002 Divergent | n/a |
| S05 | `GitCommands.Logging.CommandLogEntry::get_Detail()` | EQ003 Unknown (timeout, method-scoped) | no (blast-radius miss) |
| S06 | `GitCommands.GitRevisionInfoProvider::LoadChildren(GitUIPluginInterfaces.IGitItem)` | EQ003 Unknown (opaque, line-scoped) | yes |
| S07 | `GitCommands.EncodingHelper::GetString(byte[],byte[],System.Text.Encoding)` | EQ003 Unknown (abstraction, method-scoped) | no (blast-radius miss) |
| S08 | `GitUI.Globals::IsInRange(int,int,int)` | EQ002 Divergent | n/a |

- Seeded recall: 8/8 = 100.0%. Blast-radius misses (Unknown that does not point at the seeded line): 2.
- Seeded run totals: EQ001 12470, EQ002 86, EQ003 732, EQ004 15, EQ005 167, EQ006 272.

## Mechanical seeds (seeded-mech, ticket M4-010)
- Seeds: requested 300, applied 300, dropped (failed to compile) 17
- **Preserving family** (behaviour unchanged by construction):

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| Commute | 13 | 1 | 8 | 3 | 1 |
| InlineTemporary | 40 | 33 | 3 | 4 | 0 |
| IntroduceTemporary | 38 | 26 | 9 | 1 | 2 |
| InvertIf | 52 | 38 | 12 | 1 | 1 |
| RenameLocals | 50 | 38 | 6 | 4 | 2 |
| ReorderIndependentStatements | 1 | 0 | 0 | 0 | 1 |
| **all Preserving** | 194 | 136 | 38 | 13 | 7 |

- **Preserving Equivalent share** (`Equivalent / applied`): 136 of 194 = 70.1%. Reported only: ADR 0028 sets no threshold for it.
- **Changing family**:

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| ChangeConstant | 2 | 0 | 0 | 2 | 0 |
| DropFieldWrite | 34 | 0 | 18 | 16 | 0 |
| FlipComparison | 24 | 0 | 18 | 4 | 2 |
| SwapArguments | 46 | 5 | 26 | 14 | 1 |
| **all Changing** | 106 | 5 | 62 | 36 | 3 |


- Changing family reading (reported only): 36 Divergent, 62 Unknown, 5 Equivalent, 3 no result, of 106.
- Recall = (EQ002 + line-scoped EQ003 on the seed's line) / confirmed behaviour-changing seeds: **not computed**. `seeds.json` records each seed's method start line, not the mutated line, so "on the seed's line" cannot be checked, and no run of the repos' tests confirmed which Changing seeds changed behaviour. The counts above are the reading (P2-063).
- Changing seeds reported Equivalent are not counted as misses. Each was read as a source diff against the unseeded modern side: they are operand swaps of `==`, `!=`, `&&` or an enum-flag `|`, which cannot change behaviour, so they are unconfirmed equivalent mutants (identities only, in `.corpus/`'s `seeds.json`).
- Unconfirmed list size: 5

## Replay (full --execute, M4-009; exit 1)
- Run totals: EQ001 12475, EQ002 139, EQ003 668, EQ004 15, EQ005 167, EQ006 278; pair-level crashes 0
- Divergent results with a replay value: 362. By value: `not-constructible` 352, `not-applicable` 6, `reproduced` 4; `not-reproduced` 0
- `not-constructible` reasons: call trace 139, not public 111, heap map, cast, typeof or field 57, no public parameterless constructor 29, not a method or property getter 7, no argument can be built 7, by-reference parameter 1, both sides threw the same exception 1
- Unknown became Divergent by observation (`proofMethod: observed`): 55
- Differential testing of Unknown pairs: 129 completed, 520 `notConstructible`

## Findings
- No pair-level crash: 0 of 13541 pairs, against 92 in M4-007. The five crash families (P2-031 to P2-034, P2-026) are gone.
- No `not-reproduced` replay: 0, against 16 in M4-007 (P2-037, P2-038).
- No hand-written or mechanical seed is a confirmed miss. Five Changing seeds came back Equivalent; all five are commutative operand swaps (see above).
- Three Preserving seeds turned an Equivalent result into Divergent (EQ002): S150 `FormDeleteTag::EnableOrDisableRemotesCombobox`, S177 `SubmoduleNode::DisplayText`, S186 `PowerShellHelper::RunPowerShell` (`InlineTemporary`, `Commute`, `Commute`). Filed as P2-061. Four other Preserving seeds were already Divergent without any seed (S001, S109, S182, S247), so they are results of the migration, not of the seed.
- `DelegateCreation` alone is 17.0% of changed pairs and has no open owner: P2-067.
- The SARIF does not say why an opaque fragment is opaque, so the abstractions histogram cannot group them by reason: P2-062.
- `seeds.json` records the method's first line, not the mutated line: P2-063.
