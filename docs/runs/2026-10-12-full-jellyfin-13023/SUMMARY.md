# full run: jellyfin-13023

- Pair: human, jellyfin/jellyfin PR #13023 (net8.0 to net9.0, a pure bump), legacy 5e8c0fe40c0e, modern ceb850c77052
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #13023)
- equiv: 1d767f54, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 164s (2m44s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 unverified procedures, 0 tool execution notifications. The box ran nothing else.
- A second run in compare mode thorough followed on the same build and checkouts (`--jobs 4`, 1725s, exit 1, no crash). Every number below is the quick run's unless it says thorough; the thorough run's differences are under "Thorough run".
- Ticket P2-124. Earlier summaries of this pair: `docs/runs/2026-10-03-full-jellyfin-13023/SUMMARY.md` (P2-066) and `docs/runs/2026-10-08-full-jellyfin-13023/SUMMARY.md`. `docs/runs/2026-10-12-upgrade-verdict.md` sets this run beside P2-066's.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 37 | 0.001 | +0.000 |
| load-modern | 37 | 0.000 | +0.000 |
| enumerate | 2 | 0.514 | n/a |
| match | 1 | 0.020 | n/a |
| lower | 14398 | 79.560 | -34.808 |
| verify | 14398 | 49.407 | +5.851 |
| write | 1 | 0.241 | +0.000 |

## Load
- Projects: legacy 37 with a detected runtime, modern 37; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy none; modern none
- Runtimes detected (`run.properties.runtimes`, all from the target framework attribute): legacy net8.0 (37); modern net9.0 (37)
- Restore: `dotnet restore --configfile <this repo>/NuGet.config` on both sides, as in P2-066 (P2-115 is still open).

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14398 | 14398 |
| analysed lines | 156095 | 156036 |

- Matched pairs 14398; without opaque 12837 (89.2%); whole-body opaque 96 (0.7%); congruent 14276 (99.2%)
- Unchanged share: 99.2% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is also 99.2%: 1748 of 1762 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 99.2%. Not the row above; ADR 0034.
- Matched pairs are 135 fewer than on 2026-10-08 (14533): code the regex source generator emits is no longer a procedure (`run.properties.generatedProcedures`: legacy 145, modern 143; ticket P2-118).

Top opaque reasons (up to 15). Owning tickets were not looked up again for this rerun, except where the verdict names one.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 634 | 634 | n/a |
| CaughtException | 237 | 237 | n/a |
| Binary | 197 | 197 | n/a |
| switch-pattern | 158 | 158 | n/a |
| DefaultValue | 85 | 85 | n/a |
| ArrayCreation | 71 | 71 | n/a |
| call-throw-in-try | 70 | 70 | n/a |
| DelegateCreation | 67 | 67 | n/a |
| iterator | 65 | 65 | n/a |
| ArrayElementReference | 51 | 51 | n/a |
| Layout | 49 | 49 | n/a |
| CompoundAssignment | 44 | 44 | n/a |
| Tuple | 43 | 43 | n/a |
| DeconstructionAssignment | 39 | 39 | n/a |
| ImplicitIndexerReference | 39 | 39 | n/a |

## Changed code
- Changed pairs 122 of 14398; without opaque 62; whole-body opaque 5
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 50.8%
- 24 changed pairs are in the 14 files the pull request edits; 98 are in byte-identical files (P2-066: 49 and 524).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 62 | n/a |
| "Conversion" | 8 | n/a |
| "rebound-call" | 4 | n/a |
| "CaughtException" | 3 | n/a |
| "CaughtException+rethrow" | 3 | n/a |
| "Conversion+switch-pattern" | 3 | n/a |
| "iterator" | 3 | n/a |
| "ArrayCreation+ArrayElementReference" | 2 | n/a |
| "Binary" | 2 | n/a |
| "CaughtException+call-throw-in-try+rethrow" | 2 | n/a |
| "CompoundAssignment" | 2 | n/a |
| "DelegateCreation" | 2 | n/a |
| "Layout" | 2 | n/a |
| "Tuple" | 2 | n/a |
| "no-body" | 2 | P2-165 |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 7 | 7 |
| distinct members | 1 | 1 |
| pairs with any | 3 | 3 |

- Package changes: n/a. `corpus.ps1 -Packages` stops with "The property 'Files' cannot be found on this object" on both pairs of this rerun.

## Verdicts (full and seeded only)
- By rule: EQ001 14338, EQ002 1, EQ003 60, EQ004 0, EQ005 0, EQ006 0
- By proofMethod, Equivalent results: congruence 14276, bounded 51, lockstep-induction 11
- Changed pairs by outcome: proved Equivalent 62 (50.8%), Unknown 59 (48.4%; 60 less 1 `unmatched-overload`), Divergent 1 (0.8%; EQ002 1, EQ006 0)
- Unknown by scope: line 15, method 45. Line-scoped Unknown share: 25.0%
- Top Unknown reasons: timeout 22, opaque 20, abstraction 10, unaligned-loop 7, unmatched-overload 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 47, wallClock 0
- `unbound` Unknowns: 0
- Top abstractions: delegate 12, `System.String::IsNullOrWhiteSpace(string)` 4, `conv.f64.i32` 4, opaque Tuple 2, `conv.i32.f32` 2, `f32.mul` 2, `f32.div` 2, `conv.f32.i32` 2, opaque None 2, `op:System.Span`1::op_Implicit(System.Span<byte>)<byte>` 2, `conv.i32.f64` 2, `conv.u32.f64` 2, `f64.div` 2, `f64.mul` 2, `f64.add` 2
- Review list: 21 groups for 61 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 0.4%. Top five: `EQ003 timeout`: 22, `EQ003 abstraction`: 7, `EQ003 unaligned-loop`: 7, `EQ003 opaque:Conversion`: 3, `EQ003 opaque:iterator`: 3
- The one EQ002, `Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization()`, is a confirmed behaviour change (P2-066's adjudication, unchanged procedure). Adjudication of every Divergent is in the verdict file.

### Thorough run
Same build, same checkouts, `-Mode thorough` (first pass as quick; budget and IL passes at bound 3, resourceLimit 30000000, timeoutMs 600000), `--jobs 4`, 1725s, exit 1, 0 notifications.

| phase | items | seconds |
|---|---|---|
| lower | 14398 | 103.020 |
| verify | 14398 | 54.493 |
| budget | 30 | 1514.537 |
| il | 1 | 0.108 |
| contracts | 70 | 26.277 |

- By rule: EQ001 14340, EQ002 2, EQ003 57, EQ006 0. Changed pairs: proved Equivalent 64 (52.5%), Unknown 56 (45.9%), Divergent 2 (1.6%).
- The budget pass took 30 pairs and changed 12 results. 2 `timeout` became Equivalent (`bounded`): `MediaBrowser.Controller.MediaEncoding.EncodingHelper::GetProgressiveAudioFullCommandLine(...)` and the five-parameter `MediaBrowser.Providers.Manager.ProviderManager::SaveImage(...)`. 5 `timeout` became `abstraction` and 3 became `opaque`. 1 `unaligned-loop` became EQ002: `Jellyfin.Networking.Manager.NetworkManager::EnforceBindSettings(MediaBrowser.Common.Net.NetworkConfiguration)`, a false positive (verdict file; P2-166).
- Unknown by reason: opaque 23, abstraction 15, timeout 12, unaligned-loop 6, unmatched-overload 1.

## Tests (full only)
- Not run: the pair has no `verify_command`. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a
- `--execute` was not run: Microsoft.NETCore.App 8 and 9 are not installed (ticket Out of scope).

## Findings
- No EQ006: every runtime-change row now has a change point (P2-113), and none whose change point is net9.0 fires. The 102 EQ006 of P2-066 are 101 congruent and 1 proved.
- `Emby.Server.Implementations.ApplicationHost::GetCertificate(string,string)`, a confirmed behaviour change that P2-066's run reported EQ002, is now Unknown (`abstraction`, on `System.String::IsNullOrWhiteSpace(string)`), in both compare modes. It is not reported Equivalent. Recorded in the verdict; no ticket (the result is not wrong).
- 2 `no-body` Unknowns are unedited `extern` methods, Unknown on any pair that crosses a runtime (ADR 0054 decision 4): P2-165.
- Thorough only: one false EQ002 from the budget pass on a body whose text is unchanged: P2-166.
- `corpus.ps1 -Packages` fails on this pair (see Package changes). Tooling only; no result depends on it.
