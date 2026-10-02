# full run: gitextensions-9860

- Pair: human, gitextensions/gitextensions PR #9860 ("Bump to .NET 6.0"), legacy bcd0c2617bdd, modern 37797ea4dd74
- Corpus list: `tools/corpus/pairs.csv` (version-upgrade pair, bump with fixes; ticket P2-066)
- Migrated by: human (upstream PR #9860, net5.0-windows to net6.0-windows)
- equiv: 46e6636, mode full, wall-clock 24447s (6h47m), exit 5. Exit 5 is three pair-level lowering crashes (below); the SARIF was written and every other pair was verified. The run shared the machine with the `jellyfin-13023` run and with other sessions' `equiv` runs.

## Phase times
`write` ends 1h55m after `verify` does: the ADR 0036 contracts pass runs in between and has no phase of its own (P2-076).

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 43 | 0.000 | +0.000 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.531 | n/a |
| match | 1 | 0.027 | n/a |
| lower | 14020 | 75.151 | +38.552 |
| verify | 14017 | 17433.546 | +14575.153 |
| write | 1 | 0.295 | +0.000 |

## Load
- Projects: legacy 43 of 43 C# projects loaded, modern 43 of 43; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 1 (`Setup`), modern 1 (`Setup`)
- Runtimes detected (`run.properties.runtimes`, all from the target framework attribute): legacy net5.0 (43), modern net6.0 (43)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14022 | 14022 |
| analysed lines | 189310 | 189332 |

- Matched pairs 14020; without opaque 9715 (69.3%); whole-body opaque 73 (0.5%); congruent 13295 (94.8%)
- Unchanged share: 94.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 99.0%: 1607 of 1623 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 94.9%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15. The share in the last column is the reason's "alone" share of changed pairs.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 2019 | 2019 | none (15.2%) |
| switch-pattern | 1189 | 1189 | none (6.8%) |
| Binary | 655 | 655 | P2-087 (1.4%) |
| InstanceReference | 422 | 422 | none (below 5%) |
| DefaultValue | 355 | 355 | none (1.5%) |
| DelegateCreation | 233 | 237 | P2-067 (3.0%) |
| CaughtException | 124 | 124 | none (below 5%) |
| InterpolatedString | 87 | 81 | P2-086 done (3.2%) |
| DeconstructionAssignment | 76 | 76 | none (below 5%) |
| iterator | 73 | 73 | none (1.2%) |
| ArrayElementReference | 60 | 60 | none (below 5%) |
| CompoundAssignment | 59 | 59 | none (below 5%) |
| Tuple | 48 | 48 | none (below 5%) |
| ref-argument | 45 | 60 | none (below 5%) |
| ArrayCreation | 43 | 43 | none (below 5%) |

## Changed code
- Changed pairs 722 of 14020; without opaque 218; whole-body opaque 9
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 30.2%
- The pull request edits 16 `.cs` files. 25 changed pairs are in those files; the other 697 are in files that are byte-identical on both sides. A text scan of those 697 bodies finds an interpolated string in 417, including 223 of the 233 the solver proved: on net5.0 an interpolated string binds to `string.Format`, on net6.0 to `DefaultInterpolatedStringHandler`, so the two sides lower differently and lose congruence, and the solver then proves most of them. 56 of the 697 are EQ006 on rows with no change point (below).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 218 | n/a |
| "Conversion" | 110 | none |
| "switch-pattern" | 49 | none |
| "Conversion+DelegateCreation" | 25 | P2-067 |
| "rebound-call" | 25 | P2-070 |
| "InterpolatedString" | 23 | P2-086 done |
| "DelegateCreation" | 22 | P2-067 |
| "Conversion+DefaultValue" | 19 | none |
| "Conversion+switch-pattern" | 14 | none |
| "InterpolatedString+switch-pattern" | 12 | none |
| "Binary+Conversion" | 11 | P2-087 |
| "DefaultValue" | 11 | none |
| "Binary" | 10 | P2-087 |
| "DelegateCreation+switch-pattern" | 9 | P2-067 |
| "iterator" | 9 | none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 280 | 280 |
| distinct members | 47 | 47 |
| pairs with any | 202 | 202 |

- Package changes: 20 version changed, 3 legacy only, 1 modern only. Version changed, first 15:
  `appinsights.windowsdesktop 2.17.10 -> 2.18.1`, `dotnetruntimebootstrapper 2.0.2 -> 2.0.3`,
  `envdte 16.10.31320.204 -> 17.0.32112.339`, `gitinfo 2.1.2 -> 2.2.0`,
  `microsoft.applicationinsights 2.17.0 -> 2.18.0`, `microsoft.applicationinsights.dependencycollector 2.17.0 -> 2.18.0`,
  `microsoft.codeanalysis.common 3.10.0 -> 4.0.1`, `microsoft.codeanalysis.workspaces.common 3.10.0 -> 4.0.1`,
  `microsoft.codecoverage 16.6.1 -> 17.0.0`, `microsoft.net.test.sdk 16.6.1 -> 17.0.0`,
  `microsoft.testplatform.objectmodel 16.6.1 -> 17.0.0`, `microsoft.testplatform.testhost 16.6.1 -> 17.0.0`,
  `microsoft.toolkit.highperformance 7.0.2 -> 7.1.2`, `microsoft.visualstudio.threading 16.10.56 -> 17.0.64`,
  `microsoft.visualstudio.threading.analyzers 16.10.56 -> 17.0.64`

## Verdicts (full and seeded only)
- By rule: EQ001 13534, EQ002 3, EQ003 441, EQ004 2, EQ005 2, EQ006 59
- By proofMethod: congruence 13295, bounded 227, lockstep-induction 12
- Changed pairs by outcome: proved Equivalent 239 (33.1%; bounded 227, lockstep-induction 12), Unknown 421 (58.3%; 441 less 20 `unmatched-overload`), Divergent 62 (8.6%; EQ002 3, EQ006 59)
- Unknown by scope: line 226, method 215. Line-scoped Unknown share: 51.2%
- Top Unknown reasons: opaque 252 (line 226, method 26), timeout 99, abstraction 52, unmatched-overload 20, unaligned-loop 16, recursion 2
- `unbound` Unknowns: 0
- Top abstractions: delegate 52, opaque switch-pattern 25, opaque Conversion 21, `conv.f32.f64` 12, `f64.mul` 10, `f64.div` 10, `op:System.String::op_Inequality(string,string)` 8, opaque DefaultValue 6, `f32.sub` 6, `f32.add` 5, `f32.mul` 4, `conv.f64.i32` 4, opaque ArrayElementReference 4, `f32.div` 4, `conv.f64.f32` 4
- Review list: 73 groups for 503 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 3.6%. Top five: `EQ003 timeout`: 99, `EQ003 opaque:InterpolatedString`: 50, `EQ003 opaque:Conversion`: 45, `EQ003 opaque:DelegateCreation`: 45, `EQ003 abstraction`: 28
- EQ006 by row and the adjudication of every EQ002 and EQ006: `docs/runs/2026-10-02-upgrade-verdict.md`

## Tests (full only)
- No `verify_command` for this pair. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Replay (`--execute`)
- Not run. Neither side's runtime is installed: the box has Microsoft.NETCore.App 6.0.36 and 10.0.x, and Microsoft.WindowsDesktop.App 10.0.x only, so net5.0-windows and net6.0-windows both lack a runtime. ADR 0040 decision 3 forbids running either on another version, and the ticket asks for `--execute` only where both runtimes are installed.

## Findings
- **Three pair-level lowering crashes**: `NullReferenceException` lowering `GitCommands.CommitDataManager::TryGetCommitLog(string,string,out string,out string,bool)`, `GitCommands.AppSettings::GetGitExtensionsFullPath()` and `GitExtUtils.GitArgumentBuilder::ToString()`. The same three crash on P2-058's `gitextensions-11284`, and PR #352 files them ("Five procedures make the lowerer throw a null reference").
- **54 of 59 EQ006 cite a row with no change point**: P2-100. ADR 0040 applies such a row whenever the runtimes differ, and each of the 32 rows records a difference between .NET Framework 4.8 and .NET 10. On a .NET-to-.NET pair most of them cannot differ.
- **Three EQ006 on net6.0 rows are false positives**: two belong to P2-075 (a row that matches members its change does not touch: enumerating `ListView.Groups`, and reading `TreeNodeCollection`'s indexer where only assigning a null node reaches the change), one is P2-101 (`FileStream.Position` read where the stream had no asynchronous read or write).
- **`BugReporter.Program::Main()` is EQ002 with byte-identical source**: it passes the generated `ThisAssembly.Git.Sha` constant, which differs between the two commits. PR #352 files the cause ("A constant that only says where or from which commit the code was built is not a divergence").
- **Slow pairs**: `GitUI.CommandsDialogs.FormRemotes::InitializeComponent()` again took about 40 minutes, and the contracts pass took 1h55m after `verify` ended. Both are P2-076's measurements (PR #345 in progress).
- `Conversion` alone is 15.2% of changed pairs and `switch-pattern` alone 6.8%, with no open owner. Reported only: P2-066 does not ask for owners of opaque reasons.
- Restoring this pair needed no workaround. Fetching it needed `-Fetch`'s wider `global.json` patch (this ticket).
