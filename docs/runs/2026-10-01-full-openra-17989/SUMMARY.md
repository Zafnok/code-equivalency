# full+execute run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989, legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair, an optional extra; ticket P2-065)
- Migrated by: human (upstream PR #17989, Mono/net472 to .NET 5 on Windows)
- equiv: ef79ff6, wall-clock full 150s exit 5; full --execute 105s exit 5; census (`--lower-only`) 107s exit 5. **Neither `full` run wrote a SARIF.** Both loaded and lowered every pair, then died with an unhandled `NullReferenceException` before the first pair was verified (P2-082). The census was added so the pair has its lowering numbers; its exit 5 is eight pair-level lowering crashes (P2-083) and it did write its SARIF. The three runs ran one after another.

## Phase times
From the `full` run, which has no verify or write phase because it crashed between lower and verify.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.000 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.927 | n/a |
| match | 1 | 0.041 | n/a |
| lower | 10114 | 127.126 | +11.158 |

## Load
- Projects: legacy 9 of 9 C# projects loaded, modern 8 of 8; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration, ADR 0028 clarification 2026-09-24): legacy 1 (`OpenRA.WindowsLauncher`), modern 2 (`OpenRA.Test`, `OpenRA.WindowsLauncher`)
- Runtimes detected: legacy net472 (9), modern net5.0 (4) and netstandard2.1 (4)

## Census
From the `--lower-only` run.

| | legacy | modern |
|---|---|---|
| procedures | 10176 | 10145 |
| analysed lines | 131090 | 130228 |

- Matched pairs 10114; without opaque 6735 (66.6%); whole-body opaque 332 (3.3%); congruent 9688 (95.8%)
- Unchanged share: 95.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 98.9%: 1306 of 1309 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 95.9%. Not the row above; ADR 0034. The two differ by the eight pairs that crashed in lowering.

Top opaque reasons (legacy / modern), up to 15. The share in the last column is the reason's "alone" share of changed pairs.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 1497 | 1497 | P2-067 (26.3%) |
| Binary | 709 | 709 | none open (ADR 0039's fallback is off by default after P1-018); 6.2%, this run files P2-087 |
| InstanceReference | 565 | 565 | none (4.3%, below 5%) |
| iterator | 332 | 332 | none (2.4%, below 5%) |
| ArrayElementReference | 245 | 245 | none (3.8%, below 5%) |
| Conversion | 219 | 219 | none (0.2%, below 5%) |
| DefaultValue | 102 | 102 | none (0.0%) |
| ArrayCreation | 78 | 78 | none (0.0%) |
| CompoundAssignment | 74 | 74 | none (1.2%, below 5%) |
| CaughtException | 68 | 68 | none (2.2%, below 5%) |
| Tuple | 46 | 46 | none (0.7%, below 5%) |
| ref-argument | 39 | 39 | none (0.2%, below 5%) |
| None | 24 | 24 | none (not in a changed pair) |
| IsType | 21 | 21 | none (not in a changed pair) |
| TypeOf | 21 | 21 | none (0.0%) |

## Changed code
- Changed pairs 418 of 10114; without opaque 146; whole-body opaque 10
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 34.9%
- Only 3 of the 1309 legacy `.cs` files differ on the modern side, yet 418 pairs are not congruent. Why is not established by this run (a census reports the count, not the cause); P2-082's fix is needed before the verdicts can say.

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 146 | n/a |
| "DelegateCreation" | 110 | P2-067 |
| "Binary" | 26 | none open; this run files P2-087 |
| "InstanceReference" | 18 | none (below 5%) |
| "ArrayElementReference" | 16 | none (below 5%) |
| "Binary+DelegateCreation" | 16 | P2-067; P2-087 |
| "iterator" | 10 | none (below 5%) |
| "CaughtException" | 9 | none (below 5%) |
| "ArrayElementReference+DelegateCreation" | 5 | P2-067 |
| "CaughtException+DelegateCreation" | 5 | P2-067 |
| "CompoundAssignment" | 5 | none (below 5%) |
| "Binary+CompoundAssignment+DelegateCreation" | 3 | P2-067; P2-087 |
| "Binary+Conversion" | 3 | P2-087 |
| "Conversion+DelegateCreation" | 3 | P2-067 |
| "Tuple" | 3 | none (below 5%) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 431 | 430 |
| distinct members | 74 | 74 |
| pairs with any | 217 | 216 |

- Package changes: 0 version changed, 10 legacy only, 23 modern only

## Verdicts (full and seeded only)
- n/a: the `full` run crashed before verifying any pair and wrote no SARIF (P2-082). By rule, by proofMethod, Unknown by scope and reason, top abstractions: all n/a.
- The census run's own results: EQ004 31, EQ005 62.
- Pair-level crashes: 8 tool-execution notifications and 8 unverified procedures in the census; then the run-level crash in `full`.
- Review list: n/a (P2-064 is not done; no verdicts)

## Tests (full only)
- No `verify_command` in the manifest; the modern solution does not build `OpenRA.Test`. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Replay (full --execute, M4-009; exit 5)
- n/a: the `--execute` run crashed at the same point as `full`, before any pair was verified, and wrote no SARIF. Replay counts: none.
- The modern side targets net5.0, whose runtime is not installed on the box (6.0.36 and 10.0.x are). This run never got far enough for that to matter.

## Findings
- **Run-level crash, no output: P2-082.** After lowering all 10,114 pairs, `CompareCommand.Verified` weighs every pair for the progress log before its per-pair `try`. `PairWeight.Of` calls `IrLoopAnalysis.Of`, which throws `NullReferenceException` at `IrLoopAnalysis.Search` (`IrLoopAnalysis.cs` line 102) on one lowered procedure. Nothing catches it, so the run ends with exit 5 and no SARIF. The stack was captured with a .NET startup hook that logs first-chance exceptions; nothing under `src/` was changed. The same crash ends the Duplicati run.
- **Eight pair-level lowering crashes: P2-083.** `NullReferenceException` in `IrLowerer.Binary` (`IrLowerer.cs` line 1349, seven pairs) or in `PureCatalogue.Binary` (`PureCatalogue.cs` line 104, one pair), reached from `IrLowerer.Branch`: `OpenRA.Graphics.SheetBuilder::Allocate`, `OpenRA.Mods.Cnc.Activities.Teleport::ChooseBestDestinationCell`, `OpenRA.Mods.Cnc.Traits.PortableChrono::ResolveOrder`, `OpenRA.Platforms.Default.Sdl2Input::PumpInput`, `OpenRA.Mods.Common.Activities.Move::Tick`, `OpenRA.Mods.Common.Activities.Move.MoveFirstHalf::OnComplete`, `OpenRA.Mods.Common.Activities.UnloadCargo::Tick`, `OpenRA.Mods.Common.Orders.UnitOrderTargeter::CanTarget`.
- `Binary` alone is 6.2% of changed pairs (26 of 418) and has no open owner: P2-087. `DelegateCreation` alone is 26.3%, owned by P2-067.
- No `not-reproduced` replay (no replay ran).
