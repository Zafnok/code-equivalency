# P2-065 verdict: the five migration pairs that had never had a full run (2026-10-01)

`duplicati-3124`, `openra-17989`, `eshop-manual`, `eshop-upgrade-assistant` and
`eshop-porting-assistant`, each fetched fresh and run in `full` mode and then `full --execute`, at
equiv `ef79ff6`. The runs went one after another; no two overlapped on a checkout. Per-pair detail is
in `docs/runs/2026-10-01-full-<slug>/SUMMARY.md`. Nothing under `src/`, `tests/` or `tools/` was
changed.

These five pairs are optional extras (`equiv-corpus-run` section 2). **They take no part in ADR
0028's rule table**: no threshold is applied to them, no agent median includes them, and this file
has no continue, re-scope or stop line. `docs/runs/2026-09-30-full-verdict.md` remains the verdict.

## Outcome in one line

**None of the five runs finished clean.** Two crashed with no output, one finished with three
crashed pairs, and two compared 2 procedures because the migrated project does not compile.

| Pair | Kind | `full` exit | `--execute` exit | What happened |
|---|---|---|---|---|
| duplicati-3124 | human | 5, no SARIF | 5, no SARIF | Run-level crash after lowering, before the first verdict (P2-082). 8 lowering crashes (P2-083). One placeholder project counted as a load failure (P2-084). |
| openra-17989 | human | 5, no SARIF | 5, no SARIF | Run-level crash after lowering, before the first verdict (P2-082). 8 lowering crashes (P2-083). |
| eshop-manual | human | 5 | 5 | Completed, with 3 lowering crashes (P2-083). |
| eshop-upgrade-assistant | tool | 4 | 4 | The tool's output does not compile, so the web project is skipped: 2 pairs compared (P2-085). |
| eshop-porting-assistant | tool | 4 | 4 | The tool's output does not compile or parse, so the web project is skipped: 2 pairs compared (P2-085). |

## The table

Shares of changed pairs are over ADR 0034's changed pairs (`changedPairs`). An Unknown that is
`unmatched-overload` is not a matched pair and is left out of the Unknown share. "Flagged" is
EQ002 + EQ003 + EQ006 results over matched pairs.

| Pair | Kind | Matched pairs | Changed pairs | Proved Equivalent | Divergent | Unknown | Flagged, share of matched pairs |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 (P2-046, `bd8e379`) | human | 13541 | 1143 | 6.7% (77) | 31.4% (359) | 61.9% (707) | 8.0% (1085) |
| duplicati-3124 | human | 6275 | 936 | n/a: crashed | n/a | n/a | n/a |
| openra-17989 | human | 10114 | 418 | n/a: crashed | n/a | n/a | n/a |
| eshop-manual | human | 119 | 26 | 0% (0) | 50.0% (13) | 50.0% (13) | 30.3% (36) |
| eshop-upgrade-assistant | tool | 2 | 2 | 0% (0) | 100% (2) | 0% (0) | 100% (2) |
| eshop-porting-assistant | tool | 2 | 2 | 0% (0) | 100% (2) | 0% (0) | 100% (2) |

- Git Extensions' row is P2-046's `full` run: EQ002 84 + EQ006 275 Divergent, 726 Unknown less 19
  `unmatched-overload`, 77 solver-proved Equivalent; the three add up to its 1,143 changed pairs.
- Duplicati's and OpenRA's matched and changed pairs come from a `--lower-only` census, added because
  their `full` runs wrote nothing. A census gives no verdict.
- The two tool rows describe one small library project. Both of its pairs are the binary-serialization
  helpers, flagged EQ006. They say nothing about the application.

What the census can compare, on the three large human pairs:

| Pair | Unchanged share (`pairsCongruent` / matched) | Pair-level figure (1 - changed / matched) | Lowerable share (changed pairs) | Largest opaque reason alone | Project load rate |
|---|---|---|---|---|---|
| gitextensions-8522 | 91.6% | 91.6% | 39.1% (447 of 1143) | DelegateCreation 17.0% | 100% |
| duplicati-3124 | 85.0% | 85.1% | 48.5% (454 of 936) | DelegateCreation 11.5% | 98.1% |
| openra-17989 | 95.8% | 95.9% | 34.9% (146 of 418) | DelegateCreation 26.3% | 100% |
| eshop-manual | 75.6% | 78.2% | 46.2% (12 of 26) | DelegateCreation 23.1% | 100% |

`runtimeChangeCalls`, pairs with any (legacy / modern): Git Extensions 684 / 686, Duplicati
542 / 541, OpenRA 217 / 216, eshop-manual 9 / 9, each tool pair 2 / 2. Package changes are in each
SUMMARY.

`--execute`: no replay ran on Duplicati or OpenRA. On eshop-manual all 13 Divergent results are
`not-constructible` (9 differ only in the call trace), no Unknown became Divergent, and all 13
differential tests are `notConstructible`. On the tool pairs both replays are `not-constructible`.
There is no `not-reproduced` replay anywhere.

## Was Git Extensions typical?

**In shape, yes. In outcome, no, and that is the finding.**

- *Shape.* Where the three large human migrations can be compared, which is the census, Git
  Extensions sits in the middle. Its unchanged share is 91.6%, between Duplicati's 85.0% and OpenRA's
  95.8%. Its lowerable share is 39.1%, between OpenRA's 34.9% and Duplicati's 48.5%. The same reason,
  `DelegateCreation`, is the largest on every pair. So P2-046's reading of where the work is (most
  pairs unchanged, most changed pairs opaque, delegates first) holds beyond one repository.
- *Outcome.* Git Extensions is the only real migration `equiv` gets through. It is also the pair
  every crash fix was found on and tested against: M4-007's 92 crashes became 0 there. The first
  time `equiv` met two other codebases of the same size, one lowered procedure ended each run before
  a single verdict. Git Extensions' "0 crashes" was a fact about that pair, not about the tool.
- *Verdicts.* Whether its verdict mix (6.7% proved, 31.4% Divergent, 61.9% Unknown) is typical cannot
  be said: the two comparable pairs have no verdicts. The one pair that has them, eshop-manual, is not
  comparable: it is 119 pairs, and its hosting code was rewritten from MVC 5 to ASP.NET Core. It
  proves none of its 26 changed pairs and flags 30.3% of matched pairs against Git Extensions' 8.0%.
- *The product's use case.* A tool migrates, and `equiv` says what to check: on both pairs of that
  kind `equiv` checked 2 procedures of 185. The tool's output does not compile, which is normal for
  raw tool output, and a project that does not compile is skipped whole.

So the "prove it, then narrow what's left" goal still rests on one data point for verdicts. This run
adds two data points for the census, and it shows that the next step is not a rate but getting a
second and third migration to finish at all: P2-082 first, then P2-083, then a rerun of this ticket's
two large pairs (P2-082's last criterion asks for it).

## Findings

Every pair-level crash, every `not-reproduced` replay and every opaque reason at or above 5% of a
pair's changed pairs with no open owner is a ticket (P2-065 criterion 5). A load rate below 100% on a
human pair is a ticket (ADR 0028).

| Ticket | Finding | Pairs |
|---|---|---|
| P2-082 (S) | Weighing a pair for the progress log throws in `IrLoopAnalysis.Search`, outside the per-pair `try`: exit 5, no SARIF | duplicati-3124, openra-17989 |
| P2-083 (S) | Lowering a binary operator in a branch condition throws `NullReferenceException` (`PureCatalogue.Binary` line 104, `IrLowerer.Binary` line 1349): 19 pairs | eshop-manual 3, openra-17989 8, duplicati-3124 8 |
| P2-084 (S) | A placeholder project with one empty class counts as a load failure: load rate 98.1% on a human pair | duplicati-3124 |
| P2-085 (L) | A modern project that does not compile is skipped whole, so raw migration-tool output gets no comparison | eshop-upgrade-assistant, eshop-porting-assistant |
| P2-086 (M) | `InterpolatedString` alone is 19.2% of changed pairs; no open owner since P1-018 left the IL fallback off | eshop-manual (2.1% on Duplicati, 2.3% on Git Extensions) |
| P2-087 (M) | `Binary` alone is 6.2% of changed pairs; no open owner since P1-018 | openra-17989 (4.0% on Duplicati, 3.4% on Git Extensions) |
| P2-088 (S) | `AnonymousObjectCreation` alone is 7.7% of changed pairs (2 of 26); P2-024 is done, so no open owner | eshop-manual (0.3% on Duplicati) |

`DelegateCreation` is above 5% on every pair with changed code and is owned by P2-067. No
`not-reproduced` replay, so no ticket for one. Divergent results were not adjudicated (P2-047's
scope).

## Method notes

- **Fetch and restore.** `corpus.ps1 -Prepare`, then `-Fetch` for each pair, into a new `.corpus/`.
  `corpus.ps1` needed no fix. Both sides were restored as the skill says. Every restore succeeded
  except `eshop-porting-assistant`'s modern side (NU1605, a package downgrade the tool left behind).
- **One checkout, three pairs.** The three eShop pairs share one checkout and one legacy solution.
  They ran strictly in sequence.
- **Order of runs.** Five `full` runs in sequence, then five `--execute` runs in sequence, then the
  two census runs. A first `--execute` attempt failed on a mistake in the run script (the flag
  reached the CLI split into characters, a usage error, exit 3 within two seconds, before any load).
  Those five attempts held no data and were discarded; the `--execute` runs reported here are the
  second attempt.
- **The crash stack.** The CLI prints only an exception's message. The stack for P2-082 came from a
  diagnostic rerun of the two large pairs with a .NET startup hook (`DOTNET_STARTUP_HOOKS`) that logs
  first-chance `NullReferenceException` stacks. The hook lives outside the repository. Those two
  reruns are not counted as runs.
- **The census runs** are an addition to the ticket's two modes, made so the two crashed pairs have
  numbers for the table. They use `--lower-only`, which stops before the step that crashes.
- **Runtimes for `--execute`.** The box has .NET Framework 4.8, Microsoft.NETCore.App 6.0.36 and
  10.0.x, and Microsoft.AspNetCore.App 10.0.12 only. Duplicati's and OpenRA's modern sides target
  net5.0, which is not installed; their runs crashed before that could matter. A rerun after P2-082
  should record what `--execute` does there.
- **Tests.** None of the five pairs has a `verify_command`; no upstream test was run.
- **Time.** Every run took under three minutes, because none reached a full verify phase on a large
  pair. These wall-clocks say nothing about how long a completed Duplicati or OpenRA run takes.
