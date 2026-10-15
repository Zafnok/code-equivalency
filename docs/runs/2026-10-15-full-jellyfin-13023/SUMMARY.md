# full run: jellyfin-13023

- Pair: human, jellyfin/jellyfin PR #13023 (net8.0 to net9.0, a pure bump), legacy 5e8c0fe40c0e, modern ceb850c77052
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bfbf413e, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 168s (2m48s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-212: the five large pairs one after the other from one build, on a box that ran nothing else. The last summary of this pair is `docs/runs/2026-10-12-full-jellyfin-13023/SUMMARY.md`; `docs/runs/2026-10-15-precision-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 37 | 0.001 | +0.001 |
| load-modern | 37 | 0.000 | +0.000 |
| enumerate | 2 | 0.543 | n/a |
| match | 1 | 0.022 | n/a |
| lower | 14398 | 84.561 | -35.706 |
| verify | 14398 | 49.485 | +5.930 |
| write | 1 | 0.252 | +0.000 |

## Load
- Projects: legacy 37 with a detected runtime, modern 37; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy none; modern none
- Restore: `dotnet restore --configfile <this repo>/NuGet.config` on both sides, as in P2-124 (P2-115 is still open).
- Runtimes detected (`run.properties.runtimes`): legacy net8.0 (37) [source: attribute 37]; modern net9.0 (37) [source: attribute 37]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14398 | 14398 |
| analysed lines | 156095 | 156036 |

- Matched pairs 14398; without opaque 12847 (89.2%); whole-body opaque 96 (0.7%); congruent 14276 (99.2%)
- Unchanged share: 99.2% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1748 of 1762 legacy `.cs` files (214771 of 222793 lines, 96.4%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 99.2%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 634 | 634 | none open (P2-123 done) |
| CaughtException | 237 | 237 | P2-180 |
| Binary | 197 | 197 | none open (P2-087 done) |
| switch-pattern | 158 | 158 | none open (P2-122 done) |
| DefaultValue | 85 | 85 | none open (P2-138 done) |
| ArrayCreation | 71 | 71 | none |
| call-throw-in-try | 70 | 70 | none |
| DelegateCreation | 67 | 67 | P2-182 (OpenRA forms; P2-136 done) |
| iterator | 65 | 65 | none |
| ArrayElementReference | 51 | 51 | P2-183 (OpenRA forms) |
| Layout | 49 | 49 | none |
| CompoundAssignment | 44 | 44 | none open (P2-141 done) |
| Tuple | 43 | 43 | none |
| ImplicitIndexerReference | 39 | 39 | none |
| CollectionExpression | 37 | 37 | none |

## Changed code
- Changed pairs 122 of 14398; without opaque 63; whole-body opaque 5
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 51.6%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 63 | n/a |
| "Conversion" | 8 | none open (P2-123 done) |
| "rebound-call" | 4 | P2-179 |
| "Binary" | 3 | none open (P2-087 done) |
| "CaughtException" | 3 | P2-180 |
| "CaughtException+rethrow" | 3 | P2-180; none |
| "Conversion+switch-pattern" | 3 | none open (P2-123 done); none open (P2-122 done) |
| "iterator" | 3 | none |
| "ArrayCreation+ArrayElementReference" | 2 | none; P2-183 (OpenRA forms) |
| "CaughtException+call-throw-in-try+rethrow" | 2 | P2-180; none; none |
| "CompoundAssignment" | 2 | none open (P2-141 done) |
| "DelegateCreation" | 2 | P2-182 (OpenRA forms; P2-136 done) |
| "Layout" | 2 | none |
| "Tuple" | 2 | none |
| "no-body" | 2 | none open (P2-165 done) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 7 | 7 |
| distinct members | 1 | 1 |
| pairs with any | 3 | 3 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only

## Verdicts (full and seeded only)
- By rule: EQ001 14339, EQ002 1, EQ003 59, EQ004 0, EQ005 0, EQ006 0
- By proofMethod, Equivalent results: congruence 14276, bounded 52, lockstep-induction 11
- Changed pairs by outcome: proved Equivalent by the solver 63 (51.6%), Unknown 58 (47.5%), Divergent 1 (0.8%; EQ002 1, EQ006 0). `unmatched-overload` Unknowns, not matched pairs: 1
- Unknown by scope: line 14, method 44. Line-scoped Unknown share: 24.1%
- Top Unknown reasons: timeout 22, opaque 19, abstraction 10, unaligned-loop 7
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 47, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): callee 89, type 14, runtime-rule 10, operation 6, symbol 2, no-fingerprint 1. Of the 10 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 are EQ006, 0 EQ002 and 10 Unknown (timeout 5, abstraction 4, opaque 1).
- `unbound` Unknowns: 0
- Top abstractions: delegate 12, `System.String::IsNullOrWhiteSpace(string)` 4, `conv.f64.i32` 4, opaque Tuple 2, `conv.i32.f32` 2, `f32.mul` 2, `f32.div` 2, `conv.f32.i32` 2, opaque None 2, `op:System.Span`1::op_Implicit(System.Span<byte>)<byte>` 2, `conv.i32.f64` 2, `conv.u32.f64` 2, `f64.div` 2, `f64.mul` 2, `f64.add` 2
- Review list: 20 groups for 60 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 0.4%. Top five: `EQ003 timeout`: 22, `EQ003 abstraction`: 7, `EQ003 unaligned-loop`: 7, `EQ003 opaque:Conversion`: 3, `EQ003 opaque:iterator`: 3
- Divergent adjudication: `docs/runs/2026-10-15-precision-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown.
- The one Divergent is P2-124's confirmed `StartupHelpers::PerformStaticInitialization()`. Precision 100% (1 of 1).
- `Conversion` alone in 8 of 122 changed pairs (6.6%), no open owner: P2-215 (new).
