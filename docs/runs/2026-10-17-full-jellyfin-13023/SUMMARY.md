# full run: jellyfin-13023

- Pair: human, jellyfin/jellyfin PR #13023 (net8.0 to net9.0, a pure bump), legacy 5e8c0fe40c0e, modern ceb850c77052
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: b0f56ff8, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 173s (2m53s), exit 1. Exit 1 means Divergent results exist (here a witnessed runtime exposure counts as one, ADR 0060 decision 5). No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-238: the five large pairs one after the other from one Release build, on the checkouts P2-212 fetched and restored, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-15-full-jellyfin-13023/SUMMARY.md`; `docs/runs/2026-10-17-runtime-exposure-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 37 | 0.001 | +0.000 |
| load-modern | 37 | 0.000 | +0.000 |
| enumerate | 2 | 0.556 | n/a |
| match | 1 | 0.021 | n/a |
| lower | 14398 | 87.159 | -36.581 |
| verify | 14398 | 50.562 | +1.715 |
| write | 1 | 0.258 | +0.000 |

## Load
- Projects: legacy 37 with a detected runtime, modern 37; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net8.0 (37) [source: attribute 37]; modern net9.0 (37) [source: attribute 37]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14398 | 14398 |
| analysed lines | 156095 | 156036 |

- Matched pairs 14398; without opaque 12862 (89.3%); whole-body opaque 96 (0.7%); congruent 14276 (99.2%)
- Unchanged share: 99.2% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1748 of 1762 legacy `.cs` files (214771 of 222793 lines, 96.4%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 99.2%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Conversion | 634 | 634 | none open (P2-123 done) |
| CaughtException | 236 | 236 | none open (P2-180 done) |
| Binary | 197 | 197 | none open (P2-087 done) |
| switch-pattern | 158 | 158 | none open (P2-122 done) |
| DefaultValue | 85 | 85 | none open (P2-138 done) |
| ArrayCreation | 71 | 71 | none |
| call-throw-in-try | 70 | 70 | none |
| iterator | 65 | 65 | none |
| ArrayElementReference | 51 | 51 | P2-183 (OpenRA forms) |
| Layout | 49 | 49 | none |
| CompoundAssignment | 44 | 44 | none open (P2-141 done) |
| Tuple | 43 | 43 | none |
| ImplicitIndexerReference | 39 | 39 | none |
| CollectionExpression | 37 | 37 | none |
| DelegateCreation | 35 | 35 | none open (P2-182 done) |

## Changed code
- Changed pairs 122 of 14398; without opaque 64; whole-body opaque 5
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 52.5%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 64 | n/a |
| "Conversion" | 8 | none open (P2-123 done) |
| "rebound-call" | 4 | none open (P2-179 done) |
| "Binary" | 3 | none open (P2-087 done) |
| "CaughtException" | 3 | none open (P2-180 done) |
| "CaughtException+rethrow" | 3 | none open (P2-180 done); none |
| "Conversion+switch-pattern" | 3 | none open (P2-123 done); none open (P2-122 done) |
| "iterator" | 3 | none |
| "ArrayCreation+ArrayElementReference" | 2 | none; P2-183 (OpenRA forms) |
| "CaughtException+call-throw-in-try+rethrow" | 2 | none open (P2-180 done); none; none |
| "CompoundAssignment" | 2 | none open (P2-141 done) |
| "Layout" | 2 | none |
| "Tuple" | 2 | none |
| "no-body" | 2 | none open (P2-165 done) |
| "ArrayCreation" | 1 | none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 7 | 7 |
| distinct members | 1 | 1 |
| pairs with any | 3 | 3 |

- Package changes: 0 version changed, 0 legacy only, 0 modern only (the same checkouts as the last summary)

## Verdicts (full and seeded only)
- By rule: EQ001 14339, EQ002 1, EQ003 49, EQ004 0, EQ005 0, EQ006 0, EQ007 10 (witnessed 0, not 10)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 14276, bounded 52, lockstep-induction 11
- Changed pairs by outcome: proved Equivalent by the solver 63 (51.6%), Unknown 48 (39.3%), Divergent 1 (0.8%; EQ002 1, EQ006 0), runtime exposure 10 (8.2%; witnessed 0, not 10). `unmatched-overload` Unknowns, not matched pairs: 1
- Unknown by scope (EQ003): line 14, method 34. Line-scoped Unknown share: 29.2%
- Top Unknown reasons (EQ003): opaque 18, timeout 17, unaligned-loop 7, abstraction 6
- Runtime exposures without a witness, by `unknownReason`: timeout 5, abstraction 4, opaque 1; by first rule (up to 8): `float-to-integer` 6, `System.Environment::SetEnvironmentVariable(` 3, `extern:cet` 1
- Runtime exposures with a witness, by first rule (up to 8): none
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 47, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): callee 89, type 14, runtime-rule 10, operation 6, symbol 2, no-fingerprint 1. Of the 10 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 EQ002 and 10 EQ007; none is EQ006 or EQ003, as ADR 0060 decision 2 has it.
- `unbound` Unknowns: 0
- Top abstractions, over every `abstraction` result (EQ003 and unwitnessed EQ007): delegate 12, `System.String::IsNullOrWhiteSpace(string)` 4, `conv.f64.i32` 4, opaque Tuple 2, `conv.i32.f32` 2, `f32.mul` 2, `f32.div` 2, `conv.f32.i32` 2, opaque None 2, `op:System.Span`1::op_Implicit(System.Span<byte>)<byte>` 2, `conv.i32.f64` 2, `conv.u32.f64` 2, `f64.div` 2, `f64.mul` 2, `f64.add` 2
- Review list: 21 groups for 60 flagged results (EQ002 + EQ003 + EQ006 + EQ007), from `run.properties.reviewList`; flagged results as a share of matched pairs: 0.4%. Top five: `EQ003 timeout`: 17, `EQ003 unaligned-loop`: 7, `EQ007 runtime-change:float-to-integer`: 6, `EQ003 abstraction`: 5, `EQ003 opaque:Conversion`: 3
- Divergent adjudication: `docs/runs/2026-10-17-runtime-exposure-verdict.md` (P2-212's sample, with P2-237's execution, split by these runs' rule ids).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-212 (`bfbf413e`): the 10 `runtime-rule` Unknowns are the 10 EQ007, none witnessed (float-to-integer 6, `Environment.SetEnvironmentVariable` 3, `extern:cet` 1, an import every .NET 9 import is reached by). Every other count is P2-212's: EQ002 1, Unknown 48 (58 with the 10 exposures), the solver proves 63.
- Divergent precision: 100% (1 of 1), P2-124's confirmed `StartupHelpers::PerformStaticInitialization()`.
