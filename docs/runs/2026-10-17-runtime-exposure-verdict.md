# P2-238 verdict: the five large pairs with runtime exposures reported as EQ007 (2026-10-17)

`gitextensions-8522` (.NET Framework 4.8 to .NET 5), `gitextensions-9860` (net5.0 to net6.0), `jellyfin-13023` (net8.0
to net9.0), `duplicati-3124` (.NET Framework to .NET 5, first step) and `openra-17989` (Mono/net472 plus .NET 5), each
run in `full` mode, compare mode quick (`compare`'s default, ADR 0052), `--jobs 4`, at equiv `ccee1fe1` (`main` at
`830831cf` plus ADR 0060's EQ007, this ticket), one after the other from one Release build, with each pair's committed
config. Per-pair detail is in `docs/runs/2026-10-17-full-<slug>/SUMMARY.md`.

The runs read the checkouts P2-212 fetched and restored (its worktree's `.corpus/`, unchanged since); only `pair.json`
was copied, and the SARIF and logs are under this worktree's `.corpus/`. They went 22:57 to 23:25 local time; nothing
else ran from this session meanwhile.

**These pairs take no part in ADR 0028's rule table here.** ADR 0060 decision 7: its criteria read an EQ007 as the verdict
it had before (witnessed as Divergent, otherwise Unknown), so no success criterion moves.
`docs/runs/2026-09-30-full-verdict.md` remains the ADR 0028 verdict.

## Outcome in one line

**831 results on unedited bodies are now runtime exposures, EQ007: 235 with a witness input (EQ006 before) and 596
without (EQ003 before). Divergent is 68 (EQ002 43, EQ006 25), 2.9% of changed pairs, and Unknown 658, 27.9%, where the same results read
as before ADR 0060 are 303 and 1,254. Every exit code is the one the same run gives without the relabelling (all five exit 1). Migration Divergent
precision over EQ002 and EQ006 is 11.1% (2 of 18), and of the sampled migration exposures 15 are confirmed and 15 false
positives.** No `runtime-rule` pair is EQ002 on any pair, so ADR 0060 decision 2's modelling case never occurs; one is
proved (on `gitextensions-8522`) and stays EQ001.

## The relabelling against ADR 0060's projection (criterion 7)

ADR 0060's per-pair projection was made from P2-212's runs (`bfbf413e`). "Measured" is these runs.

| Pair | | EQ007 witnessed | EQ007 not | EQ006 | EQ002 | Divergent | Unknown |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | projected | 124 | 351 | 13 | 23 | 36 | 205 |
| | measured | 111 | 331 | 10 | 24 | 34 | 206 |
| gitextensions-9860 | projected | 0 | 6 | 0 | 6 | 6 | 200 |
| | measured | 0 | 6 | 0 | 5 | 5 | 198 |
| jellyfin-13023 | projected | 0 | 10 | 0 | 1 | 1 | 48 |
| | measured | 0 | 10 | 0 | 1 | 1 | 48 |
| duplicati-3124 | projected | 102 | 134 | 15 | 23 | 38 | 182 |
| | measured | 91 | 130 | 14 | 13 | 27 | 168 |
| openra-17989 | projected | 33 | 119 | 1 | 0 | 1 | 43 |
| | measured | 33 | 119 | 1 | 0 | 1 | 38 |
| **All five** | projected | 259 | 620 | 29 | 53 | 82 | 678 |
| | measured | **235** | **596** | **25** | **43** | **68** | **658** |

Unknown is EQ003 without `unmatched-overload` (20, 1, 19, 6 and 0 of them), as the scoreboard counts it.

The relabelling itself is exact: on each migration the `runtime-rule` pairs P2-237 found at `de93b9c7` (8522: 111 EQ006
and 331 Unknown; Duplicati: 91 and 130; OpenRA: 33 and 119) are these runs' witnessed and unwitnessed EQ007, one for one,
and on the upgrades P2-212's (6 and 10 Unknown). Every difference from the projection is a commit merged after P2-212:

- Exposures 879 to 831: the path and row tickets merged between P2-212 and P2-237 (P2-207, P2-208, P2-216 to P2-218,
  P2-220, P2-228, P2-231, P2-232) took 33 pairs of `gitextensions-8522` and 15 of `duplicati-3124` out of the
  `runtime-rule` class (13 and 11 of them witnessed before), which is P2-237's count.
- EQ002 53 to 43: Duplicati 23 to 13, which P2-237's run at `de93b9c7` already had (P2-213's committed config lets
  P2-192 map 8 of P2-212's sampled ones); `gitextensions-9860` 6 to 5 (`PatchManager::ToPatch(...)`, P2-211);
  `gitextensions-8522` 23 to 24, also P2-237's count.
- EQ006 29 to 25, the ones whose text differs: 3 fewer on `gitextensions-8522` and 1 on Duplicati, as P2-237 found them.
- Unknown 678 to 658: Duplicati 182 to 168 (179 at P2-237; P2-180, merged since, lowered the largest caught-exception use,
  and the solver proves 11 more), OpenRA 43 to 38 (P2-182, merged since, lowered the largest DelegateCreation form; 5 more
  proved), `gitextensions-9860` 200 to 198 and `gitextensions-8522` 205 to 206 (207 at P2-237).

## Exit codes (criterion 4)

All five runs exit 1, as P2-212's and P2-237's runs of the same pairs did: each has a new witnessed exposure or a new
EQ002 or EQ006. The exit code reads the verdict, never the rule id (ADR 0060 decision 5); `CompareCommandTests`'
`ARuntimeExposureExitsAsItsVerdictDidBeforeIt` shows a run's exit code is the same with and without the relabelling,
under both `--fail-on` values.

## P2-212's sample, re-split by the new rule ids (criterion 7)

P2-212 drew 136 migration results (46 EQ002, 90 EQ006) and 7 upgrade ones (all EQ002). P2-237 executed the 90 it left
undetermined. Two splits:

- **As drawn**: each result's rule in P2-212's own SARIF, found by its fingerprint (`resultFingerprint/v1`, which no rule
  id enters), relabelled as `VerdictRule` does now; P2-212's classification.
- **Now**: the same procedure in these runs, by identity, with P2-237's classification where it re-adjudicated the
  result and P2-212's otherwise. A result that is no longer flagged at `ccee1fe1` (18 are Equivalent) is left out, as
  P2-237's "still Divergent" column leaves them out.

| Pair | Rule | As drawn: confirmed | false positive | undetermined | Now: confirmed | false positive | undetermined |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 1 | 6 | 16 | 2 | 7 | 14 |
| | EQ006 | 0 | 2 | 2 | 0 | 2 | 2 |
| | EQ007 | 0 | 9 | 17 | 3 | 5 | 14 |
| duplicati-3124 | EQ002 | 0 | 15 | 8 | 0 | 7 | 6 |
| | EQ006 | 0 | 0 | 2 | 0 | 0 | 2 |
| | EQ007 | 0 | 6 | 22 | 10 | 3 | 12 |
| openra-17989 | EQ006 | 0 | 0 | 1 | 0 | 0 | 1 |
| | EQ007 | 0 | 7 | 22 | 2 | 7 | 20 |
| **Migrations** | **EQ002 + EQ006** | **1** | **23** | **29** | **2** | **16** | **25** |
| | **EQ007** | **0** | **22** | **61** | **15** | **15** | **46** |
| gitextensions-9860 | EQ002 | 5 | 1 | 0 | 5 | 0 | 0 |
| jellyfin-13023 | EQ002 | 1 | 0 | 0 | 1 | 0 | 0 |

- **Migration Divergent precision, EQ002 and EQ006 only: 11.1% (2 of 18) now, 4.2% (1 of 24) as drawn.** ADR 0060 put it
  between 2.2% and 4.5% on P2-212's classification; as drawn it is 4.2%, EQ002 1 of 22 and EQ006 0 of 2. The scoreboard
  takes "now", P2-237's choice for the same row.
- **The exposures' own count: 15 confirmed and 15 false positives now (50.0%), 0 and 22 as drawn.** 83 of the 90 sampled
  EQ006 were exposures; execution confirmed 15 of P2-237's 17, so most of what the real runtimes showed was a runtime
  change reaching an unedited body, which is what EQ007 now says. Every sampled exposure is a witnessed one: P2-212 drew
  from EQ002 and EQ006, so none of the 596 unwitnessed exposures has been adjudicated.
- Under `--execute`, ADR 0060 decision 4 makes an exposure EQ006 only by a `reproduced` replay or an observed
  differential-testing difference. Of the 15 confirmed exposures, P2-237's runs reproduced 1 by replay
  (Duplicati's `SystemIOWindows::GetFileAttributes(string)`), which such a run reports as EQ006 with `proofMethod:
  observed`; the other 14 were confirmed with `probe` on inputs a trace chose, which no run does by itself.
- The upgrades keep P2-212's 85.7% (6 of 7). Its one false positive, `PatchManager::ToPatch(...)`, is Equivalent at this
  commit (P2-211), so "now" is 6 of 6; the row keeps the adjudicated figure, as P2-237 left it.

## Findings

- No crash, no skipped project, no `run.properties.unverified` entry and no tool execution notification on any pair.
- No `runtime-rule` pair is EQ002 on any pair: ADR 0060 decision 2's soundness or modelling case does not occur, and no
  ticket is needed for it.
- `duplicati-3124` took 613 s, from 447 s at P2-212: `verify` 536 s, the pairs P2-180 lowered now reaching the solver,
  on top of the slowdown P2-244 already owns (it measures two commits back to back). No new ticket.
- Nothing else is new: every exposure, Divergent and Unknown cause these runs show has the owner P2-212's and P2-237's
  verdicts gave it, or was cleared by it.

## Scoreboard (criterion 7)

`equiv-scoreboard` was applied over the five large pairs, all five by these runs. Rows rewritten: Proved Equivalent,
Changed pairs the solver proves, Unknown, Divergent, Runtime exposure (new), Divergent precision (migrations), Left for
a reviewer, Run time. Unchanged: the header (5 pairs, 58,477 matched), Equivalent by congruence (96.0%, 56,120),
Soundness, Large pairs that run to a result and Repeatability, and the upgrades' and cleanups' precision.

| Row | Was | Now |
|---|---|---|
| Proved Equivalent | 97.3% (56,900 of 58,477) | 97.3% (56,920 of 58,477) |
| Changed pairs the solver proves | 33.1% (780 of 2,357) | 33.9% (800 of 2,357) |
| Unknown | 2.2% of pairs (1,273), 54.0% of changed pairs; opaque 573, abstraction 346, budget 319, unaligned loop 31, recursion 4 | 1.1% of pairs (658), 27.9% of changed pairs; opaque 416, abstraction 91, budget 129, unaligned loop 18, recursion 4 |
| Divergent | 0.5% of pairs (304), 12.9% of changed pairs | 0.1% of pairs (68), 2.9% of changed pairs |
| Runtime exposure | | 1.4% of pairs (831), 35.3% of changed pairs; 235 with a witness input, 596 without; of the sampled migration exposures 15 confirmed, 15 false positive |
| Divergent precision | migrations 35.4% (17 of 48) | migrations 11.1% (2 of 18), EQ002 and EQ006 only |
| Left for a reviewer | 2.7% of pairs (1,577) | 2.7% of pairs (1,557: Unknown 658, Divergent 68, exposure 831) |
| Run time | mean 6 min (3 min to 9 min) | mean 6 min (3 min to 10 min) |

Migration Divergent precision falls because the confirmed results moved to the exposure row with their rule id, not
because a result got worse: P2-237's 17 confirmed are 2 EQ002 and 15 exposures. Run time's upper end moved with Duplicati
(above).

## Method notes

- The re-split joins "as drawn" by the 12-character fingerprint prefixes P2-212's appendix gives, against P2-212's SARIF
  (every one of the 143 is found, with the rule the appendix names), and "now" by procedure identity against these runs,
  as P2-237 did, since a fingerprint moves between commits for the same procedure.
- P2-237's runs were `--execute` runs and these are not; the "now" split counts each sampled result by its rule in a
  default run, which is what the scoreboard rests on.
