# full+execute run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124, legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair, an optional extra; ticket P2-065)
- Migrated by: human (upstream PR #3124, .NET Framework to .NET 5, first step)
- equiv: ef79ff6, wall-clock full 91s exit 5; full --execute 98s exit 5; census (`--lower-only`) 105s exit 5. **Neither `full` run wrote a SARIF.** Both loaded and lowered every pair, then died with an unhandled `NullReferenceException` before the first pair was verified (P2-082). The census was added so the pair has its lowering numbers; its exit 5 is eight pair-level lowering crashes (P2-083) and it did write its SARIF. The three runs ran one after another.

## Phase times
From the `full` run, which has no verify or write phase because it crashed between lower and verify.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.000 | +0.000 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.369 | n/a |
| match | 1 | 0.015 | n/a |
| lower | 6275 | 46.426 | +17.933 |

## Load
- Projects: legacy 50 of 51 C# projects loaded, modern 51 of 52; skipped: `Duplicati.Tools` on both sides: "the project loaded and declares at least one type, but symbol enumeration found zero procedures"
- Project load rate: 98.1% (101 of 103); legacy 98.0%, modern 98.1%
- Reason: `Duplicati.Tools` holds one source file with one empty placeholder class, there so the build accepts the project's content files. It has no method to compare on either side, and P2-018's rule counts it as a load failure. This is a human pair below 100%, so it is a ticket: P2-084.
- Runtimes detected: legacy net471 (50), modern net5.0 (51)

## Census
From the `--lower-only` run.

| | legacy | modern |
|---|---|---|
| procedures | 6340 | 6367 |
| analysed lines | 68699 | 69187 |

- Matched pairs 6275; without opaque 5094 (81.2%); whole-body opaque 126 (2.0%); congruent 5331 (85.0%)
- Unchanged share: 85.0% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 61.8%: 421 of 739 legacy `.cs` files (77615 of 125501 lines) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 85.1%. Not the row above; ADR 0034. The two differ by the eight pairs that crashed in lowering.

Top opaque reasons (legacy / modern), up to 15. The share in the last column is the reason's "alone" share of changed pairs.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 468 | 466 | P2-067 (11.5%) |
| Binary | 241 | 240 | none open (4.0%, below 5%; P2-087 is filed from OpenRA) |
| CaughtException | 192 | 193 | none (2.7%, below 5%) |
| iterator | 99 | 99 | none (3.2%, below 5%) |
| rethrow | 93 | 94 | none (0.4%, below 5%) |
| InterpolatedString | 69 | 69 | none open (2.1%, below 5%; P2-086 is filed from eshop-manual) |
| TranslatedQuery | 69 | 69 | none (1.6%, below 5%; P2-026 is done) |
| Conversion | 68 | 68 | none (1.0%, below 5%) |
| AnonymousObjectCreation | 55 | 55 | none (0.3%, below 5%; P2-088 is filed from eshop-manual) |
| ArrayCreation | 54 | 54 | none (0.4%, below 5%) |
| InstanceReference | 51 | 51 | none (0.3%, below 5%) |
| ref-argument | 40 | 40 | none (0.4%, below 5%) |
| call-throw-in-try | 39 | 39 | none (0.1%, below 5%) |
| CompoundAssignment | 30 | 30 | none (0.7%, below 5%) |
| Throw | 24 | 24 | none (0.3%, below 5%) |

## Changed code
- Changed pairs 936 of 6275; without opaque 454; whole-body opaque 31
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 48.5%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 454 | n/a |
| "DelegateCreation" | 108 | P2-067 |
| "Binary" | 37 | none open (below 5% here; P2-087) |
| "iterator" | 30 | none (below 5%) |
| "CaughtException" | 25 | none (below 5%) |
| "InterpolatedString" | 20 | none open (below 5% here; P2-086) |
| "TranslatedQuery" | 15 | none (below 5%) |
| "CaughtException+DelegateCreation" | 13 | P2-067 |
| "AnonymousObjectCreation+DelegateCreation" | 11 | P2-067; P2-088 |
| "Binary+CaughtException+rethrow" | 11 | P2-087 |
| "Conversion" | 9 | none (below 5%) |
| "Binary+CaughtException" | 8 | P2-087 |
| "DefaultValue" | 8 | none (below 5%) |
| "CompoundAssignment" | 7 | none (below 5%) |
| "DelegateCreation+InterpolatedString" | 6 | P2-067; P2-086 |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1346 | 1342 |
| distinct members | 133 | 134 |
| pairs with any | 542 | 541 |

- Package changes: 16 version changed, 9 legacy only, 145 modern only. Version-changed (first 15): alphavss 1.4.0 -> 2.0.0; awssdk.core 3.3.103.37 -> 3.3.104.11; awssdk.identitymanagement 3.3.103.26 -> 3.3.104.18; awssdk.s3 3.3.104.25 -> 3.3.110.7; fluentftp 27.1.1 -> 28.0.5; mailkit 2.3.1.6 -> 2.4.1; microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18; mimekit 2.3.1 -> 2.4.1; minio 3.1.7 -> 3.1.8; newtonsoft.json 12.0.2 -> 12.0.3; ngettext 0.6.4 -> 0.6.5; sharpaescrypt.dll 1.3.3 -> 1.3.4; ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1; system.reactive 4.0.0 -> 4.1.2, 4.3.2; system.reactive.linq 4.0.0 -> 4.3.2

## Verdicts (full and seeded only)
- n/a: the `full` run crashed before verifying any pair and wrote no SARIF (P2-082). By rule, by proofMethod, Unknown by scope and reason, top abstractions: all n/a.
- The census run's own results: EQ003 6, EQ004 92, EQ005 65.
- Pair-level crashes: 8 lowering notifications and 8 unverified procedures in the census (plus the two skipped-project notifications); then the run-level crash in `full`.
- Review list: n/a (P2-064 is not done; no verdicts)

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Replay (full --execute, M4-009; exit 5)
- n/a: the `--execute` run crashed at the same point as `full`, before any pair was verified, and wrote no SARIF. Replay counts: none.
- The modern side targets net5.0, whose runtime is not installed on the box (6.0.36 and 10.0.x are). This run never got far enough for that to matter.

## Findings
- **Run-level crash, no output: P2-082.** The same crash as OpenRA: after lowering all 6,275 pairs, `PairWeight.Of` (called from `CompareCommand.Verified`, outside the per-pair `try`) throws `NullReferenceException` at `IrLoopAnalysis.Search` (`IrLoopAnalysis.cs` line 102). Exit 5, no SARIF.
- **Eight pair-level lowering crashes: P2-083.** `NullReferenceException` in `PureCatalogue.Binary` (`PureCatalogue.cs` line 104, five pairs) or `IrLowerer.Binary` (`IrLowerer.cs` line 1349, three pairs), reached from `IrLowerer.Branch`: `Duplicati.Library.Main.Controller::OnOperationComplete(object)`, `Duplicati.Library.Main.Volumes.FilesetVolumeReader.ControlFileEnumerable.ControlFileEnumerator::Dispose()` and `::MoveNext()`, `Duplicati.Library.Main.Volumes.IndexVolumeReader.IndexBlockVolumeEnumerable.IndexBlockVolumeEnumerator.BlockEnumerable.BlockEnumerator::ReadVolumeProps()`, `Duplicati.Library.Main.Volumes.IndexVolumeReader.IndexBlockVolumeEnumerable.IndexBlockVolumeEnumerator.IndexBlockVolume::ReadVolumeProps()`, `Duplicati.Library.Snapshots.LinuxSnapshot::FindSnapshotByLocalPath(string)`, `Duplicati.Library.Modules.Builtin.RunScript::Execute`, `Duplicati.Library.UsageReporter.Reporter::get_MaxReportLevel()`.
- **Project load rate 98.1% on a human pair: P2-084.** A placeholder project with one empty class is counted as a load failure on both sides.
- No opaque reason other than `DelegateCreation` (11.5% alone, P2-067) reaches 5% of changed pairs here.
- No `not-reproduced` replay (no replay ran).
