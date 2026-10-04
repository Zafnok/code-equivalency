# full run: jellyfin-13023

- Pair: human, jellyfin/jellyfin PR #13023 ("Update projects to .NET 9"), legacy 5e8c0fe40c0e, modern ceb850c77052
- Corpus list: `tools/corpus/pairs.csv` (version-upgrade pair, pure bump; ticket P2-066)
- Migrated by: human (upstream PR #13023, net8.0 to net9.0)
- equiv: 8e0ed3c, mode full, wall-clock 5529s (1h32m), exit 5. Exit 5 is three pair-level lowering crashes (below); the SARIF was written and every other pair was verified. The run shared the machine with the `gitextensions-9860` run. A first run at 46e6636, before P2-076, was stopped by the user after 12h17m: it had spent 11h02m on one pair, `Emby.Server.Implementations.Data.SqliteItemRepository::GetWhereClauses(MediaBrowser.Controller.Entities.InternalItemsQuery,Microsoft.Data.Sqlite.SqliteCommand)`, inside `Z3Backend.Inline` and before any solver check (two stack samples). At 8e0ed3c that pair takes 189 s and is Unknown (`timeout`).

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 37 | 0.000 | +0.000 |
| load-modern | 37 | 0.000 | +0.000 |
| enumerate | 2 | 0.577 | n/a |
| match | 1 | 0.022 | n/a |
| lower | 14503 | 110.181 | -53.814 |
| verify | 14500 | 4858.379 | -639.328 |
| contracts | 406 | 518.720 | -124.303 |
| write | 1 | 0.374 | +0.000 |

## Load
- Projects: legacy 37 of 37 C# projects loaded, modern 37 of 37; skipped: none
- Project load rate: 100%
- Not built: none on either side
- Runtimes detected (`run.properties.runtimes`, all from the target framework attribute): legacy net8.0 (37), modern net9.0 (37)
- Restore: the skill's `dotnet restore` fails on both sides with NU1100, because this repository's NuGet source mapping reaches `.corpus/` and Jellyfin names its source `nuget`. Restored with `--configfile <this repo>/nuget.config` (P2-115).

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14504 | 14503 |
| analysed lines | 156095 | 156036 |

- Matched pairs 14503; without opaque 11889 (82.0%); whole-body opaque 182 (1.3%); congruent 13927 (96.0%)
- Unchanged share: 96.0% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 99.2%: 1748 of 1762 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 96.0%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15. The share in the last column is the reason's "alone" share of changed pairs.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 1015 | 1014 | P2-123 (5.1%) |
| switch-pattern | 991 | 991 | P2-122 (3.8%) |
| Binary | 497 | 497 | P2-087 (4.0%) |
| DefaultValue | 261 | 261 | none (below 5%) |
| CaughtException | 228 | 228 | none (below 5%) |
| DelegateCreation | 197 | 197 | P2-067 done (9.2%) |
| CompoundAssignment | 91 | 91 | none (below 5%) |
| await-using | 83 | 83 | none (2.3%) |
| rebound-call | 69 | 69 | P2-070 (1.9%) |
| ArrayCreation | 68 | 68 | none (below 5%) |
| call-throw-in-try | 64 | 64 | none (below 5%) |
| iterator | 64 | 64 | none (below 5%) |
| ArrayElementReference | 53 | 53 | none (below 5%) |
| Tuple | 42 | 42 | none (below 5%) |
| DeconstructionAssignment | 39 | 39 | none (below 5%) |

## Changed code
- Changed pairs 573 of 14503; without opaque 134; whole-body opaque 50
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 23.4%
- The pull request edits 14 `.cs` files, each by one or two lines. 49 changed pairs are in those files; the other 524 are in files that are byte-identical on both sides. A text scan of those 524 bodies finds a `string.Equals` or `Equals` call that takes a `StringComparison` in 353: the row for those two members has no change point, so it applies to any runtime difference and takes the pair out of congruence (P2-113). 63 of the 524 hold a call that binds to a different overload on net9.0 (below).
- Calls rebound by the newer reference assemblies, from `reboundCalls`, by form: `String.Format(IFormatProvider,string,object[])` to the `ReadOnlySpan<object>` overload 16; `TimeSpan.FromHours(double)` to `FromHours(int)` 16; `TimeSpan.FromMilliseconds(double)` to `FromMilliseconds(long,long)` 14; `TimeSpan.FromMinutes(double)` to `FromMinutes(long)` 10; `TimeSpan.FromSeconds(double)` to `FromSeconds(long)` 9; `IReadOnlyCollection<T>.Count` to `List<T>.Count` 4 (the PR's one field-type edit); `String.Contains(char)` to `Enumerable.Contains<char>` 2; `String.Join(char,string[])` to the `ReadOnlySpan<string>` overload 2. P2-070 owns rebinding (PR #359 in progress).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 134 | n/a |
| "DelegateCreation" | 53 | P2-067 done |
| "DelegateCreation+switch-pattern" | 33 | none |
| "no-body" | 30 | P2-118 (`[GeneratedRegex]` partial methods) |
| "Conversion" | 29 | P2-123 |
| "Binary" | 23 | P2-087 |
| "switch-pattern" | 22 | P2-122 |
| "await-using" | 13 | none |
| "Conversion+switch-pattern" | 12 | none |
| "rebound-call+switch-pattern" | 12 | P2-070 |
| "rebound-call" | 11 | P2-070 |
| "Conversion+DelegateCreation" | 10 | none |
| "Conversion+DelegateCreation+switch-pattern" | 10 | none |
| "Binary+Conversion+switch-pattern" | 7 | P2-087 |
| "Binary+DelegateCreation+switch-pattern" | 7 | P2-087 |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 817 | 817 |
| distinct members | 14 | 14 |
| pairs with any | 278 | 278 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only

## Verdicts (full and seeded only)
- By rule: EQ001 13927, EQ002 9, EQ003 525, EQ004 0, EQ005 1, EQ006 102
- By proofMethod: congruence 13927. No pair was proved by the solver.
- Changed pairs by outcome: proved Equivalent 0 (0%), Unknown 462 (80.6%; 525 less 63 `unmatched-overload`), Divergent 111 (19.4%; EQ002 9, EQ006 102)
- Unknown by scope: line 165, method 360. Line-scoped Unknown share: 31.4%
- Top Unknown reasons: opaque 243 (line 165, method 78), timeout 138, unmatched-overload 63, abstraction 52, unaligned-loop 29
- `unbound` Unknowns: 0
- Top abstractions: delegate 49, opaque Binary 36, opaque switch-pattern 29, opaque Conversion 14, opaque ImplicitIndexerReference 10, `op:System.String::op_Implicit(string)` 9, `f64.div` 4, `conv.i32.f64` 4, `f64.mul` 4, opaque Tuple 3, `conv.i32.f32` 2, `f32.mul` 2, `f32.div` 2, `conv.f32.i32` 2, `op:System.String::op_Inequality(string,string)` 2
- Review list: 65 groups for 636 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 4.4%. Top five: `EQ003 timeout`: 138, `EQ003 opaque:DelegateCreation`: 101, `EQ006 runtime-change:System.String::Equals(string,string,System.StringComparison)`: 83, `EQ003 unmatched-overload`: 63, `EQ003 opaque:rebound-call`: 32
- EQ006 by row and the adjudication of every EQ002 and EQ006: `docs/runs/2026-10-03-upgrade-verdict.md`

## Tests (full only)
- No `verify_command` for this pair. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Replay (`--execute`)
- Not run. Neither side's runtime is installed: the box has Microsoft.NETCore.App 6.0.36 and 10.0.x and Microsoft.AspNetCore.App 10.0.12 only. ADR 0040 decision 3 forbids running either side on another version, and the ticket asks for `--execute` only where both runtimes are installed.

## Findings
- **No changed pair is proved.** Every pair that is not congruent ends Unknown or Divergent, on a pull request that edits 14 files by a line or two each.
- **All 102 EQ006 cite a row with no change point**, 92 of them `String.Equals` with a `StringComparison`: P2-113. No row whose change point is net9.0 fired.
- **Three pair-level lowering crashes**, "Cannot transcode invalid UTF-16 string to UTF-8 JSON text", all in code the regex source generator emits: P2-116.
- **Generated regex code is compared as written code**: 4 EQ002, 8 Unknown, 4 `unmatched-overload` and 1 Removed that nobody wrote, plus the 30 `no-body` pairs, 29 of them `[GeneratedRegex]` partial methods: P2-118.
- **Two false EQ002 on generic `Enum` members** (`Enum.GetValues<T>()`, `Enum.IsDefined<T>(v)`): P2-117.
- **58 `unmatched-overload` results on 29 controller actions that have one method each**: P2-119.
- **Calls rebound by .NET 9's new overloads** keep 63 unchanged bodies out of congruence: P2-070 (forms above).
- **`ProviderManager::SaveImage(...)` is EQ002 for an added `ConfigureAwait(false)`**: P2-071, which merged after this build (8bf3aa1). Not rerun.
- **What the pull request really changed**, and how it was reported: two behaviour changes are EQ002 (`StartupHelpers::PerformStaticInitialization()`, `ApplicationHost::GetCertificate(string,string)`), and two are Unknown (`PluginManager::TryGetPluginDlls(...)`, `timeout`; `ProbeResultNormalizer::GetMpegTimestamp(string)`, `abstraction`). None is reported Equivalent.
- `DelegateCreation` alone is 9.2% of changed pairs, `no-body` 5.2% (P2-118) and `Conversion` 5.1% (P2-123, filed after this run).
